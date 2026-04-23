using Be.Fgov.Ehealth.Samws.V2.Export;

namespace Samparse;

public record QualificationMember(string ProfessionalCv, string? NlName, string? FrName);

public record QualificationListInfo(string ListKey, IReadOnlyList<QualificationMember> Members);

/// <summary>
/// Resolves a <c>PurchasingAdvisorQualList</c> key (e.g. <c>"962"</c>) to a list of
/// Professional CV codes with their human-readable NL/FR names.
/// </summary>
/// <remarks>
/// Resolution chain:
/// <code>
///   QualificationList (CHAPTERIV)                // list of authorised CV codes
///     → ProfessionalAuthorisation[].Data.ProfessionalCv  (date-filtered)
///     → REF.ProfessionalCode[cv].NameId           // lookup in reference data
///     → CHAPTERIV.NameExplanation[NameId]         // human-readable translations
///     → NameTranslation[LanguageCV].Data.ShortText (date-filtered)
/// </code>
/// The qualification list itself carries a NameId too, but in practice its translations
/// are internal admin labels (<c>"list verse 58425"</c>), so this resolver reports on the
/// CV members instead — that is the real semantic content.
/// </remarks>
public sealed class QualificationListResolver
{
    private readonly Dictionary<string, QualificationListFullDataType> _listsByKey;
    private readonly Dictionary<string, long> _profCodeToNameId;
    private readonly Dictionary<long, NameExplanationFullDataType> _nameById;

    public QualificationListResolver(ExportChapterIvType chapterIv, ExportReferencesType references)
    {
        _listsByKey = chapterIv.QualificationList
            .GroupBy(q => q.QualificationList)
            .ToDictionary(g => g.Key, g => g.First());
        _profCodeToNameId = references.ProfessionalCode
            .GroupBy(p => p.ProfessionalCv)
            .ToDictionary(g => g.Key, g => g.First().NameId);
        _nameById = chapterIv.NameExplanation
            .GroupBy(n => n.NameId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public QualificationListInfo? Resolve(string listKey, DateTime date)
    {
        if (!_listsByKey.TryGetValue(listKey, out var list)) return null;

        var activeCvs = list.ProfessionalAuthorisation
            .SelectMany(pa => pa.Data
                .Where(d => IsActive(d, date))
                .Select(d => d.ProfessionalCv))
            .Distinct()
            .OrderBy(cv => cv, StringComparer.Ordinal)
            .ToList();

        return new QualificationListInfo(listKey, activeCvs.Select(cv => ResolveCv(cv, date)).ToList());
    }

    public QualificationMember ResolveCv(string cv, DateTime date) =>
        _profCodeToNameId.TryGetValue(cv, out var nameId) && _nameById.TryGetValue(nameId, out var exp)
            ? new QualificationMember(cv, Translation(exp, "NL", date), Translation(exp, "FR", date))
            : new QualificationMember(cv, null, null);

    private static string? Translation(NameExplanationFullDataType exp, string lang, DateTime date) =>
        exp.NameTranslation
            .FirstOrDefault(t => t.LanguageCv.Equals(lang, StringComparison.OrdinalIgnoreCase))
            ?.Data
            .Where(d => IsActive(d, date))
            .OrderByDescending(d => d.From)
            .FirstOrDefault()?.ShortText;

    private static bool IsActive(DataPeriodType d, DateTime date) =>
        d.From <= date && (!d.ToSpecified || d.To >= date);
}
