using Be.Fgov.Ehealth.Samws.V2.Export;
using System.Xml.Serialization;

namespace Samparse;

public record SamDatabase
{
    public ExportActualMedicinesType?    ActualMedicines   { get; init; }
    public ExportChapterIvType?          ChapterIv         { get; init; }
    public ExportCompoundingType?        Compounding       { get; init; }
    public ExportCompaniesType?          Companies         { get; init; }
    public ExportImportedMedicinesType?  ImportedMedicines { get; init; }
    public ExportNonMedicinalType?       NonMedicinal      { get; init; }
    public ExportReferencesType?         References        { get; init; }
    public ExportReimbursementsType?     Reimbursements    { get; init; }
    public ExportReimbursementLawsType?  ReimbursementLaws { get; init; }
    public ExportVirtualMedicinesType?   VirtualMedicines  { get; init; }
}

public static class SamLoader
{
    public static SamDatabase Load(string folderPath)
    {
        return new SamDatabase
        {
            ActualMedicines = Deserialize<ExportActualMedicinesType>(folderPath, "AMP"),
            ChapterIv = Deserialize<ExportChapterIvType>(folderPath, "CHAPTERIV"),
            Compounding = Deserialize<ExportCompoundingType>(folderPath, "CMP"),
            Companies = Deserialize<ExportCompaniesType>(folderPath, "CPN"),
            ImportedMedicines = Deserialize<ExportImportedMedicinesType>(folderPath, "IMPP"),
            NonMedicinal = Deserialize<ExportNonMedicinalType>(folderPath, "NONMEDICINAL"),
            References = Deserialize<ExportReferencesType>(folderPath, "REF"),
            Reimbursements = Deserialize<ExportReimbursementsType>(folderPath, "RMB"),
            ReimbursementLaws   = Deserialize<ExportReimbursementLawsType>(folderPath, "RML"),
            VirtualMedicines = Deserialize<ExportVirtualMedicinesType>(folderPath, "VMP"),
        };
    }

    private static T? Deserialize<T>(string folderPath, string prefix) where T : class
    {
        var files = Directory.GetFiles(folderPath, $"{prefix}-*.xml");
        if (files.Length == 0)
            return null;

        var serializer = new XmlSerializer(typeof(T));
        using var stream = File.OpenRead(files[0]);
        return (T?)serializer.Deserialize(stream);
    }
}
