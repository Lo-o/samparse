using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Be.Fgov.Ehealth.Samws.V2.Export;

namespace Samparse;

public enum ApplicabilityResult { Applicable, NotApplicable, Unknown }

public record PatientContext(
    SexRestrictedType? Sex = null,
    decimal? Age = null,
    DurationUnitType AgeUnit = DurationUnitType.Y);

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
            CheckAge(data, patient),
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
        var active = paragraph.Verse
            .Select(v => (v.VerseSeq, Data: v.Data.FirstOrDefault(d => ChapterIvIndex.IsActive(d, date))))
            .Where(t => t.Data is not null)
            .ToDictionary(t => t.VerseSeq, t => t.Data!);

        var childrenBy = active
            .GroupBy(kv => kv.Value.VerseSeqParent)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

        var rootSeqs = active
            .Where(kv => !active.ContainsKey(kv.Value.VerseSeqParent))
            .Select(kv => kv.Key)
            .ToList();

        // AndClauseNum: verses sharing a number must be co-selectable. If any member
        // is incompatible, the whole group is — propagate the worst back to each member.
        var andAdj = new Dictionary<int, ApplicabilityResult>();
        foreach (var group in active.Where(kv => kv.Value.AndClauseNumSpecified)
                                    .GroupBy(kv => kv.Value.AndClauseNum)
                                    .Where(g => g.Count() > 1))
        {
            var combined = Combine(group.Select(kv => VerseCompatible(kv.Value, patient, doctor, isRenewal, date, index)));
            foreach (var kv in group) andAdj[kv.Key] = combined;
        }

        ApplicabilityResult EvaluateChoice(int verseSeq)
        {
            var self = VerseCompatible(active[verseSeq], patient, doctor, isRenewal, date, index);
            if (andAdj.TryGetValue(verseSeq, out var groupAdj)) self = Worst(self, groupAdj);
            if (self == ApplicabilityResult.NotApplicable) return ApplicabilityResult.NotApplicable;

            return Worst(self, EvaluateSubtree(verseSeq));
        }

        ApplicabilityResult EvaluateSubtree(int verseSeq)
        {
            var data = active[verseSeq];
            var kids = childrenBy.GetValueOrDefault(verseSeq) ?? new List<int>();
            var (checkboxKids, narrativeKids) = kids.Aggregate(
                (cb: new List<int>(), nr: new List<int>()),
                (acc, k) => { (active[k].CheckBoxInd ? acc.cb : acc.nr).Add(k); return acc; });

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

    private static ApplicabilityResult CheckAge(VerseDataType data, PatientContext patient)
    {
        if (!data.MinimumAgeAuthorizedSpecified && !data.MaximumAgeAuthorizedSpecified)
            return ApplicabilityResult.Applicable;
        if (patient.Age is null) return ApplicabilityResult.Unknown;

        var patientDays = ToDays(patient.Age.Value, patient.AgeUnit);
        if (data.MinimumAgeAuthorizedSpecified &&
            patientDays < ToDays(data.MinimumAgeAuthorized, data.MinimumAgeAuthorizedUnit))
            return ApplicabilityResult.NotApplicable;
        if (data.MaximumAgeAuthorizedSpecified &&
            patientDays > ToDays(data.MaximumAgeAuthorized, data.MaximumAgeAuthorizedUnit))
            return ApplicabilityResult.NotApplicable;
        return ApplicabilityResult.Applicable;
    }

    private static ApplicabilityResult CheckQualification(
        VerseDataType data, DoctorContext doctor, DateTime date, ChapterIvIndex index)
    {
        if (string.IsNullOrEmpty(data.PurchasingAdvisorQualList)) return ApplicabilityResult.Applicable;
        if (doctor.ProfessionalCvs.Count == 0) return ApplicabilityResult.Unknown;
        var allowed = index.ActiveCvsFor(data.PurchasingAdvisorQualList, date);
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

    private static double ToDays(decimal value, DurationUnitType unit) => unit switch
    {
        DurationUnitType.D => (double)value,
        DurationUnitType.W => (double)value * 7,
        DurationUnitType.M => (double)value * 30.4375,
        DurationUnitType.Y => (double)value * 365.25,
        _                  => (double)value,
    };

    private static ApplicabilityResult Worst(ApplicabilityResult a, ApplicabilityResult b) =>
        a == ApplicabilityResult.NotApplicable || b == ApplicabilityResult.NotApplicable ? ApplicabilityResult.NotApplicable :
        a == ApplicabilityResult.Unknown       || b == ApplicabilityResult.Unknown       ? ApplicabilityResult.Unknown :
                                                                                            ApplicabilityResult.Applicable;

    private static ApplicabilityResult Combine(IEnumerable<ApplicabilityResult> results) =>
        results.Aggregate(ApplicabilityResult.Applicable, Worst);
}
