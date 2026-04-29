using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../SAM"));
var db = SamLoader.Load(samDir, SamExport.ChapterIv);

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
        var r = ApplicabilityChecker.CouldApplyTo(paragraph, patient, doctor, isRenewal: null, date, index);
        Console.WriteLine($"  {label,-30} -> {r}");
    }
}

Console.ReadLine();
