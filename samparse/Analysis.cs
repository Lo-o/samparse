using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Be.Fgov.Ehealth.Samws.V2.Export;

namespace Samparse;

/// <summary>
/// Ad-hoc queries over the Chapter IV data. Use these to probe hypotheses about
/// how applicability rules are distributed across paragraphs and verses.
/// </summary>
/// <remarks>
/// <para><b>Finding (April 2026 export):</b> qualification requirements are <i>not</i>
/// paragraph-level. Even restricted to "terminal" verses (those carrying
/// <c>AgreementTerm</c> — the ones that actually grant a reimbursement window),
/// paragraphs can contain multiple terminals with differing qualification lists:</para>
/// <list type="bullet">
///   <item>978 paragraphs have ≥1 terminal verse.</item>
///   <item>46 (≈4.7%) have terminals with &gt;1 distinct qualification requirement.</item>
///   <item>40 of those mix "no qualification" with a specific qualification list.</item>
///   <item>6 use two or more <i>different specific</i> qualification lists
///         within the same paragraph (e.g. <c>7320000</c>: [280, 580];
///         <c>8410000</c>: [625, 422]; <c>11440000</c>: [736, 953]).</item>
/// </list>
/// <para>This means a doctor may qualify for some reimbursement paths inside a
/// paragraph but not others — consistent with how <see cref="ApplicabilityChecker.CouldApplyTo"/>
/// already models it (paragraph is Applicable iff at least one mandatory path is satisfiable).</para>
/// </remarks>
public static class ChapterIvAnalysis
{
    public record QualificationVariance(
        string ParagraphName,
        IReadOnlyList<string> DistinctQuals,
        int VerseCount);

    /// <summary>
    /// For each paragraph, collect the set of distinct <c>PurchasingAdvisorQualList</c>
    /// values across the verse data active on <paramref name="date"/>. Empty / missing
    /// quals are reported as the bucket <c>"&lt;none&gt;"</c>.
    /// </summary>
    /// <param name="terminalOnly">
    /// When true, only verses with <c>AgreementTermSpecified == true</c> are considered.
    /// These are the "terminal" verses that actually grant a reimbursement window; narrative
    /// verses without quals are excluded so they don't falsely look like "any doctor qualifies".
    /// </param>
    public static IReadOnlyList<QualificationVariance> QualificationsPerParagraph(
        ExportChapterIvType chapterIv,
        DateTime date,
        bool terminalOnly = false)
    {
        return chapterIv.Paragraph
            .Select(p =>
            {
                var considered = p.Verse
                    .Select(v => v.Data.FirstOrDefault(d => IsActive(d, date)))
                    .Where(d => d is not null && (!terminalOnly || d.AgreementTermSpecified))
                    .Select(d => d!)
                    .ToList();

                var distinct = considered
                    .Select(d => string.IsNullOrEmpty(d.PurchasingAdvisorQualList) ? NoneBucket : d.PurchasingAdvisorQualList)
                    .Distinct()
                    .ToList();

                return new QualificationVariance(p.ParagraphName, distinct, considered.Count);
            })
            .ToList();
    }

    public const string NoneBucket = "<none>";

    private static bool IsActive(DataPeriodType d, DateTime date) =>
        d.From <= date && (!d.ToSpecified || d.To >= date);
}
