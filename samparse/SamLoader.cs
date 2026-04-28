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

/// <summary>
/// Selector for which SAM exports to deserialize. Loading the full dataset
/// is ~2.4 GB of XML and ~8–12 GB of object graph in memory; ask only for
/// what the caller actually uses.
/// </summary>
[Flags]
public enum SamExport
{
    None              = 0,
    ActualMedicines   = 1 << 0,
    ChapterIv         = 1 << 1,
    Compounding       = 1 << 2,
    Companies         = 1 << 3,
    ImportedMedicines = 1 << 4,
    NonMedicinal      = 1 << 5,
    References        = 1 << 6,
    Reimbursements    = 1 << 7,
    ReimbursementLaws = 1 << 8,
    VirtualMedicines  = 1 << 9,
    All               = ActualMedicines | ChapterIv | Compounding | Companies | ImportedMedicines
                      | NonMedicinal | References | Reimbursements | ReimbursementLaws | VirtualMedicines,
}

public static class SamLoader
{
    public static SamDatabase Load(string folderPath, SamExport which = SamExport.All)
    {
        return new SamDatabase
        {
            ActualMedicines   = which.HasFlag(SamExport.ActualMedicines)   ? Deserialize<ExportActualMedicinesType>(folderPath, "AMP") : null,
            ChapterIv         = which.HasFlag(SamExport.ChapterIv)         ? Deserialize<ExportChapterIvType>(folderPath, "CHAPTERIV") : null,
            Compounding       = which.HasFlag(SamExport.Compounding)       ? Deserialize<ExportCompoundingType>(folderPath, "CMP") : null,
            Companies         = which.HasFlag(SamExport.Companies)         ? Deserialize<ExportCompaniesType>(folderPath, "CPN") : null,
            ImportedMedicines = which.HasFlag(SamExport.ImportedMedicines) ? Deserialize<ExportImportedMedicinesType>(folderPath, "IMPP") : null,
            NonMedicinal      = which.HasFlag(SamExport.NonMedicinal)      ? Deserialize<ExportNonMedicinalType>(folderPath, "NONMEDICINAL") : null,
            References        = which.HasFlag(SamExport.References)        ? Deserialize<ExportReferencesType>(folderPath, "REF") : null,
            Reimbursements    = which.HasFlag(SamExport.Reimbursements)    ? Deserialize<ExportReimbursementsType>(folderPath, "RMB") : null,
            ReimbursementLaws = which.HasFlag(SamExport.ReimbursementLaws) ? Deserialize<ExportReimbursementLawsType>(folderPath, "RML") : null,
            VirtualMedicines  = which.HasFlag(SamExport.VirtualMedicines)  ? Deserialize<ExportVirtualMedicinesType>(folderPath, "VMP") : null,
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
