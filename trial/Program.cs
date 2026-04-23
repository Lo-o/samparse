using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../SAM"));
var db = SamLoader.Load(samDir);

Console.WriteLine("Done. Press Enter to exit.");

// Quick applicability smoke test
var index = new ChapterIvIndex(db.ChapterIv!);
var date = DateTime.Today;

foreach (var paragraphName in new[] { "12850000", "13780000" })
{
    var paragraph = db.ChapterIv!.Paragraph.FirstOrDefault(p => p.ParagraphName == paragraphName);
    if (paragraph is null) { Console.WriteLine($"{paragraphName}: not found"); continue; }

    Console.WriteLine($"\nParagraph {paragraphName}:");
    foreach (var (label, patient, doctor) in new (string, PatientContext, DoctorContext)[]
    {
        ("unknown patient",          new PatientContext(),                                DoctorContext.Unknown),
        ("female, 70 yr",            new PatientContext(SexRestrictedType.F, 70),         DoctorContext.Unknown),
        ("male, 70 yr",              new PatientContext(SexRestrictedType.M, 70),         DoctorContext.Unknown),
        ("female, 10 yr",            new PatientContext(SexRestrictedType.F, 10),         DoctorContext.Unknown),
        ("female, 70 yr + CV 668",   new PatientContext(SexRestrictedType.F, 70),         new DoctorContext(["668"])),
        ("female, 70 yr + CV 001",   new PatientContext(SexRestrictedType.F, 70),         new DoctorContext(["001"])),
    })
    {
        var r = ApplicabilityChecker.CouldApplyTo(paragraph, patient, doctor, date, index);
        Console.WriteLine($"  {label,-30} -> {r}");
    }
}

var osteoporosis12850000 = db.ChapterIv.Paragraph
    .First(p => p.ParagraphName == "12850000");

var osteoporosisVerses = osteoporosis12850000.Verse
    .OrderBy(verse => verse.VerseSeq)
    .SelectMany(vers => vers.Data)
    .Where(data => data.From < DateTimeOffset.UtcNow && (data.To > DateTimeOffset.Now || data.To == DateTime.MinValue));



var allParagraphs = db.ChapterIv.Paragraph;


var onduidelijkWatQualificationListPreciesIs = db.ChapterIv.QualificationList;



var paragraphDate = DateTimeOffset.Now;


// Qualification-variance analysis — see Samparse.ChapterIvAnalysis for method + findings.
Console.WriteLine("\n=== Terminal-verse qualification variance ===");
var terminals = ChapterIvAnalysis.QualificationsPerParagraph(db.ChapterIv!, DateTime.Today, terminalOnly: true);
var varying = terminals.Where(t => t.DistinctQuals.Count > 1).ToList();

Console.WriteLine($"Paragraphs with ≥1 terminal verse: {terminals.Count(t => t.VerseCount > 0)}");
Console.WriteLine($"  of which have >1 distinct qual across terminals: {varying.Count}");
Console.WriteLine($"  mixing '<none>' with a specific qual: {varying.Count(t => t.DistinctQuals.Contains(ChapterIvAnalysis.NoneBucket))}");
Console.WriteLine($"  with 2+ different specific quals: {varying.Count(t => t.DistinctQuals.Count(q => q != ChapterIvAnalysis.NoneBucket) >= 2)}");

Console.WriteLine("\nFirst 10 varying paragraphs:");
foreach (var t in varying.Take(10))
    Console.WriteLine($"  {t.ParagraphName}: [{string.Join(", ", t.DistinctQuals)}]  ({t.VerseCount} terminal verses)");

Console.ReadLine();



public record PatientDataRelevantForParagraph
{
    public string Sex { get; init; }
    public DateTimeOffset DateOfBirth { get; init; }
}

