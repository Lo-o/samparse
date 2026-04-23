// Ad-hoc investigations over the SAM data. Run with:
//   dotnet run --project analysis/analysis.csproj
//
// Each block is a self-contained script. Comment out whatever you don't need.

using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../trial/SAM"));
var db = SamLoader.Load(samDir);
var today = DateTime.Today;

// ---------------------------------------------------------------------------
// 1) Qualification-variance per paragraph.
//    Are there paragraphs where different terminal verses require different doctor
//    qualifications? Finding: yes, 46 of 978 paragraphs with terminals (~4.7%).
// ---------------------------------------------------------------------------
Console.WriteLine("=== Terminal-verse qualification variance ===");

var terminals = ChapterIvAnalysis.QualificationsPerParagraph(db.ChapterIv!, today, terminalOnly: true);
var varying = terminals.Where(t => t.DistinctQuals.Count > 1).ToList();

Console.WriteLine($"Paragraphs with >=1 terminal verse: {terminals.Count(t => t.VerseCount > 0)}");
Console.WriteLine($"  of which have >1 distinct qual across terminals: {varying.Count}");
Console.WriteLine($"  mixing '<none>' with a specific qual: {varying.Count(t => t.DistinctQuals.Contains(ChapterIvAnalysis.NoneBucket))}");
Console.WriteLine($"  with 2+ different specific quals: {varying.Count(t => t.DistinctQuals.Count(q => q != ChapterIvAnalysis.NoneBucket) >= 2)}");

Console.WriteLine("\nFirst 10 varying paragraphs:");
foreach (var t in varying.Take(10))
    Console.WriteLine($"  {t.ParagraphName}: [{string.Join(", ", t.DistinctQuals)}]  ({t.VerseCount} terminal verses)");

// ---------------------------------------------------------------------------
// 2) Qualification list resolution.
//    A list key (e.g. "962") by itself has no useful name. The CV codes inside do:
//    they resolve via REF.ProfessionalCode.NameId -> CHAPTERIV.NameExplanation.
// ---------------------------------------------------------------------------
Console.WriteLine("\n=== Qualification list resolution (CV codes -> NL names) ===");

var resolver = new QualificationListResolver(db.ChapterIv!, db.References!);

foreach (var key in new[] { "962", "288", "313", "422", "625", "736", "953" })
{
    var info = resolver.Resolve(key, today);
    if (info is null) { Console.WriteLine($"  List {key}: not found"); continue; }

    Console.WriteLine($"\n  Qual list {key}  —  {info.Members.Count} active CV code(s):");
    foreach (var m in info.Members.Take(6))
        Console.WriteLine($"    CV {m.ProfessionalCv,-4}  {Truncate(m.NlName ?? "(no name)", 120)}");
    if (info.Members.Count > 6) Console.WriteLine($"    ... and {info.Members.Count - 6} more");
}

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
