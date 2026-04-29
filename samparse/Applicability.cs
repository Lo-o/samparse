using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Be.Fgov.Ehealth.Samws.V2.Export;

namespace Samparse;

public enum ApplicabilityResult { Applicable, NotApplicable, Unknown }

public record PatientContext(
    SexRestrictedType? Sex = null,
    DateOnly? DateOfBirth = null);

public record DoctorContext(IReadOnlyCollection<string> ProfessionalCvs)
{
    public static readonly DoctorContext Unknown = new(Array.Empty<string>());
}

public sealed class ChapterIvIndex
{
    private readonly Dictionary<string, QualificationListFullDataType> _qualByKey;

    public ChapterIvIndex(ExportChapterIvType chapterIv)
    {
        _qualByKey = chapterIv.QualificationList
            .GroupBy(q => q.QualificationList)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public IReadOnlySet<string> ActiveCvsFor(string qualListKey, DateTime date) =>
        _qualByKey.TryGetValue(qualListKey, out var list)
            ? list.ProfessionalAuthorisation
                .SelectMany(pa => pa.Data.Where(d => IsActive(d, date)))
                .Select(d => d.ProfessionalCv)
                .ToHashSet()
            : new HashSet<string>();

    internal static bool IsActive(DataPeriodType d, DateTime date) =>
        d.From <= date && (!d.ToSpecified || d.To >= date);
}

public static class ApplicabilityChecker
{
    public static ApplicabilityResult VerseCompatible(
        VerseDataType data, PatientContext patient, DoctorContext doctor, bool? isRenewal, DateTime date, ChapterIvIndex index)
    {
        var checks = new[]
        {
            CheckSex(data, patient),
            CheckAge(data, patient, date),
            CheckQualification(data, doctor, date, index),
            CheckRequestType(data, isRenewal),
        };
        return
            checks.Any(r => r == ApplicabilityResult.NotApplicable) ? ApplicabilityResult.NotApplicable :
            checks.Any(r => r == ApplicabilityResult.Unknown)       ? ApplicabilityResult.Unknown :
                                                                      ApplicabilityResult.Applicable;
    }

    public static ApplicabilityResult CouldApplyTo(
        ParagraphFullDataType paragraph, PatientContext patient, DoctorContext doctor, bool? isRenewal, DateTime date, ChapterIvIndex index)
    {
        var activeByDate = paragraph.Verse
            .Select(v => (v.VerseSeq, Data: v.Data.FirstOrDefault(d => ChapterIvIndex.IsActive(d, date))))
            .Where(t => t.Data is not null)
            .ToDictionary(t => t.VerseSeq, t => t.Data!);

        // RequestType marks which procedural branch a verse belongs to (new vs renewal),
        // not a constraint to satisfy: a verse tagged for the off-scenario describes a
        // different path through the paragraph and should be pruned rather than fail
        // the narrative AND. Pruning is transitive — descendants of a pruned verse are
        // scoped to the same branch. With prescription type unknown we don't prune;
        // CheckRequestType then yields Unknown and propagates up.
        bool MatchesScenario(VerseDataType d) =>
            !d.RequestTypeSpecified || isRenewal is null
                || (d.RequestType == RequestTypeType.P) == isRenewal.Value;

        var dateChildrenBy = activeByDate
            .GroupBy(kv => kv.Value.VerseSeqParent)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
        var dateRoots = activeByDate
            .Where(kv => !activeByDate.ContainsKey(kv.Value.VerseSeqParent))
            .Select(kv => kv.Key);

        var activeVersesByVerseSeq = new Dictionary<int, VerseDataType>();
        var queue = new Queue<int>();
        foreach (var seq in dateRoots)
            if (MatchesScenario(activeByDate[seq])) { activeVersesByVerseSeq[seq] = activeByDate[seq]; queue.Enqueue(seq); }
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in dateChildrenBy.GetValueOrDefault(parent) ?? new List<int>())
                if (MatchesScenario(activeByDate[child])) { activeVersesByVerseSeq[child] = activeByDate[child]; queue.Enqueue(child); }
        }

        var childrenBy = activeVersesByVerseSeq
            .GroupBy(kv => kv.Value.VerseSeqParent)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

        var rootSeqs = activeVersesByVerseSeq
            .Where(kv => !activeVersesByVerseSeq.ContainsKey(kv.Value.VerseSeqParent))
            .Select(kv => kv.Key)
            .ToList();

        // AndClauseNum: verses sharing a number must be co-selectable. If any member
        // is incompatible, the whole group is — propagate the worst back to each member.
        var andAdj = new Dictionary<int, ApplicabilityResult>();
        foreach (var group in activeVersesByVerseSeq.Where(kv => kv.Value.AndClauseNumSpecified)
                                    .GroupBy(kv => kv.Value.AndClauseNum)
                                    .Where(g => g.Count() > 1))
        {
            var combined = Combine(group.Select(kv => VerseCompatible(kv.Value, patient, doctor, isRenewal, date, index)));
            foreach (var kv in group) andAdj[kv.Key] = combined;
        }

        ApplicabilityResult EvaluateChoice(int verseSeq)
        {
            var self = VerseCompatible(activeVersesByVerseSeq[verseSeq], patient, doctor, isRenewal, date, index);
            if (andAdj.TryGetValue(verseSeq, out var groupAdj)) self = Worst(self, groupAdj);
            if (self == ApplicabilityResult.NotApplicable) return ApplicabilityResult.NotApplicable;

            return Worst(self, EvaluateSubtree(verseSeq));
        }

        ApplicabilityResult EvaluateSubtree(int verseSeq)
        {
            var data = activeVersesByVerseSeq[verseSeq];
            var kids = childrenBy.GetValueOrDefault(verseSeq) ?? new List<int>();
            var (checkboxKids, narrativeKids) = kids.Aggregate(
                (cb: new List<int>(), nr: new List<int>()),
                (acc, k) => { (activeVersesByVerseSeq[k].CheckBoxInd ? acc.cb : acc.nr).Add(k); return acc; });

            var narrative = Combine(narrativeKids.Select(EvaluateChoice));
            if (narrative == ApplicabilityResult.NotApplicable) return ApplicabilityResult.NotApplicable;

            var mandatory = ApplicabilityResult.Applicable;
            if (data.MinCheckNum > 0 && checkboxKids.Count > 0)
            {
                var childResults = checkboxKids.Select(EvaluateChoice).ToList();
                var definitely = childResults.Count(r => r == ApplicabilityResult.Applicable);
                var possibly   = childResults.Count(r => r != ApplicabilityResult.NotApplicable);
                if (possibly < data.MinCheckNum) return ApplicabilityResult.NotApplicable;
                if (definitely < data.MinCheckNum) mandatory = ApplicabilityResult.Unknown;
            }

            return Worst(narrative, mandatory);
        }

        return Combine(rootSeqs.Select(EvaluateChoice));
    }

    private static ApplicabilityResult CheckSex(VerseDataType data, PatientContext patient) =>
        !data.SexRestrictedSpecified    ? ApplicabilityResult.Applicable :
        patient.Sex is null             ? ApplicabilityResult.Unknown :
        data.SexRestricted == patient.Sex ? ApplicabilityResult.Applicable :
                                              ApplicabilityResult.NotApplicable;

    private static ApplicabilityResult CheckAge(VerseDataType data, PatientContext patient, DateTime date)
    {
        if (!data.MinimumAgeAuthorizedSpecified && !data.MaximumAgeAuthorizedSpecified)
            return ApplicabilityResult.Applicable;
        if (patient.DateOfBirth is null) return ApplicabilityResult.Unknown;

        var dob = patient.DateOfBirth.Value;
        var refDate = DateOnly.FromDateTime(date);

        // Min is inclusive: patient must have reached the Nth-unit anniversary.
        if (data.MinimumAgeAuthorizedSpecified &&
            AddDuration(dob, WholeUnits(data.MinimumAgeAuthorized), data.MinimumAgeAuthorizedUnit) > refDate)
            return ApplicabilityResult.NotApplicable;
        // Max is strict: patient must not yet have reached the (N+1)th-unit anniversary.
        if (data.MaximumAgeAuthorizedSpecified &&
            AddDuration(dob, WholeUnits(data.MaximumAgeAuthorized) + 1, data.MaximumAgeAuthorizedUnit) <= refDate)
            return ApplicabilityResult.NotApplicable;

        return ApplicabilityResult.Applicable;
    }

    private static ApplicabilityResult CheckQualification(
        VerseDataType verse, DoctorContext doctor, DateTime date, ChapterIvIndex index)
    {
        if (string.IsNullOrEmpty(verse.PurchasingAdvisorQualList)) return ApplicabilityResult.Applicable;
        if (doctor.ProfessionalCvs.Count == 0) return ApplicabilityResult.Unknown;
        var allowed = index.ActiveCvsFor(verse.PurchasingAdvisorQualList, date);
        return doctor.ProfessionalCvs.Any(allowed.Contains)
            ? ApplicabilityResult.Applicable
            : ApplicabilityResult.NotApplicable;
    }

    private static ApplicabilityResult CheckRequestType(VerseDataType data, bool? isRenewal)
    {
        if (!data.RequestTypeSpecified) return ApplicabilityResult.Applicable;
        if (isRenewal is null) return ApplicabilityResult.Unknown;
        var verseIsRenewal = data.RequestType == RequestTypeType.P;
        return verseIsRenewal == isRenewal.Value
            ? ApplicabilityResult.Applicable
            : ApplicabilityResult.NotApplicable;
    }

    private static DateOnly AddDuration(DateOnly start, int n, DurationUnitType unit) => unit switch
    {
        DurationUnitType.D => start.AddDays(n),
        DurationUnitType.W => start.AddDays(n * 7),
        DurationUnitType.M => start.AddMonths(n),
        DurationUnitType.Y => start.AddYears(n),
        _                  => start.AddDays(n),
    };

    // Schema permits Decimal3d1Type but historically every value is integral.
    // Reject anything fractional so a future schema drift surfaces loudly instead of truncating.
    private static int WholeUnits(decimal value) =>
        value == Math.Floor(value)
            ? (int)value
            : throw new InvalidOperationException(
                $"Age limit {value} is not a whole number; SAM data has only contained integers.");

    private static ApplicabilityResult Worst(ApplicabilityResult a, ApplicabilityResult b) =>
        a == ApplicabilityResult.NotApplicable || b == ApplicabilityResult.NotApplicable ? ApplicabilityResult.NotApplicable :
        a == ApplicabilityResult.Unknown       || b == ApplicabilityResult.Unknown       ? ApplicabilityResult.Unknown :
                                                                                            ApplicabilityResult.Applicable;

    private static ApplicabilityResult Combine(IEnumerable<ApplicabilityResult> results) =>
        results.Aggregate(ApplicabilityResult.Applicable, Worst);
}
