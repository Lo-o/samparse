using Be.Fgov.Ehealth.Samws.V2.Chapteriv.Submit;
using Samparse;

var samDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../SAM"));
var db = SamLoader.Load(samDir, SamExport.ChapterIv);

Console.WriteLine("Done. Press Enter to exit.");

// Quick applicability smoke test
var index = new ChapterIvIndex(db.ChapterIv!);
var date = DateTime.Today;
DateOnly Born(int yearsAgo) => DateOnly.FromDateTime(date.AddYears(-yearsAgo));

foreach (var paragraphName in new[] { "12850000", "13780000", "8410000" })
{
    var paragraph = db.ChapterIv!.Paragraph.FirstOrDefault(p => p.ParagraphName == paragraphName);
    if (paragraph is null) { Console.WriteLine($"{paragraphName}: not found"); continue; }

    Console.WriteLine($"\nParagraph {paragraphName}:");
    foreach (var (label, patient, doctor) in new (string, PatientContext, DoctorContext)[]
    {
        ("unknown patient",          new PatientContext(),                                       DoctorContext.Unknown),
        ("female, 70 yr",            new PatientContext(SexRestrictedType.F, Born(70)),          DoctorContext.Unknown),
        ("male, 70 yr",              new PatientContext(SexRestrictedType.M, Born(70)),          DoctorContext.Unknown),
        ("female, 10 yr",            new PatientContext(SexRestrictedType.F, Born(10)),          DoctorContext.Unknown),
        ("female, 70 yr + CV 668",   new PatientContext(SexRestrictedType.F, Born(70)),          new DoctorContext(["668"])),
        ("female, 70 yr + CV 001",   new PatientContext(SexRestrictedType.F, Born(70)),          new DoctorContext(["001"])),
    })
    {
        var r = ApplicabilityChecker.CouldApplyTo(paragraph, patient, doctor, isRenewal: null, date, index);
        Console.WriteLine($"  {label,-30} -> {r}");
    }
}

// 8410000 — narrative siblings split by RequestType (c=N, d=P). Pre-fix, every
// concrete prescription type returned NotApplicable; post-fix the off-scenario
// sibling is pruned and the on-scenario one carries the paragraph.
{
    var p = db.ChapterIv!.Paragraph.First(x => x.ParagraphName == "8410000");
    var adultCardiologist = new PatientContext(SexRestrictedType.M, Born(60));
    var card = new DoctorContext(["730"]); // CV 730 is in qualification list 422 (cardiology)
    Console.WriteLine("\nParagraph 8410000 (RequestType-split sibling test, adult male + cardiology):");
    foreach (var isRenewal in new bool?[] { false, true, null })
        Console.WriteLine($"  isRenewal={isRenewal,-5} -> {ApplicabilityChecker.CouldApplyTo(p, adultCardiologist, card, isRenewal, date, index)}");
}

Console.ReadLine();
