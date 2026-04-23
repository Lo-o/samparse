using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../SAM"));
var db = SamLoader.Load(samDir);

Console.WriteLine("Done. Press Enter to exit.");

var osteoporosis12850000 = db.ChapterIv.Paragraph
    .First(p => p.ParagraphName == "12850000");

var osteoporosisVerses = osteoporosis12850000.Verse
    .OrderBy(verse => verse.VerseSeq)
    .SelectMany(vers => vers.Data)
    .Where(data => data.From < DateTimeOffset.UtcNow && (data.To > DateTimeOffset.Now || data.To == DateTime.MinValue));



var allParagraphs = db.ChapterIv.Paragraph;


var onduidelijkWatQualificationListPreciesIs = db.ChapterIv.QualificationList;



var paragraphDate = DateTimeOffset.Now;


Console.ReadLine();



public record PatientDataRelevantForParagraph
{
    public string Sex { get; init; }
    public DateTimeOffset DateOfBirth { get; init; }
}

