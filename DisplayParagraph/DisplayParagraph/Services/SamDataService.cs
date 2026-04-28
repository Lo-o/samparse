using System.Diagnostics;
using Samparse;

namespace DisplayParagraph.Services;

public class SamDataService
{
    public SamDatabase Database { get; }
    public ChapterIvIndex ChapterIvIndex { get; }
    public QualificationListResolver QualResolver { get; }

    // Resolved once at startup using DateTime.Today; entries that only become active later
    // in the process lifetime won't appear until restart.
    public List<QualificationMember> AllCvCodes { get; }
    public List<QualificationListInfo> AllQualLists { get; }

    public SamDataService(IConfiguration config, IWebHostEnvironment env, ILogger<SamDataService> logger)
    {
        var relPath = config["SamDataPath"] ?? "../../trial/SAM";
        var dataPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, relPath));

        const SamExport needed = SamExport.ChapterIv | SamExport.References;

        logger.LogInformation("Loading SAM exports {Exports} from {Path}…", needed, dataPath);
        var sw = Stopwatch.StartNew();

        Database = SamLoader.Load(dataPath, needed);
        ChapterIvIndex = new ChapterIvIndex(Database.ChapterIv!);
        QualResolver = new QualificationListResolver(Database.ChapterIv!, Database.References!);

        var today = DateTime.Today;
        AllCvCodes = Database.References!.ProfessionalCode
            .Select(p => QualResolver.ResolveCv(p.ProfessionalCv, today))
            .DistinctBy(m => m.ProfessionalCv)
            .OrderBy(m => m.ProfessionalCv, StringComparer.Ordinal)
            .ToList();
        AllQualLists = Database.ChapterIv!.QualificationList
            .Select(q => QualResolver.Resolve(q.QualificationList, today))
            .OfType<QualificationListInfo>()
            .OrderBy(i => i.ListKey, StringComparer.Ordinal)
            .ToList();

        logger.LogInformation("SAM data loaded in {Elapsed:N1}s ({CvCount} CV codes, {ListCount} qual lists)",
            sw.Elapsed.TotalSeconds, AllCvCodes.Count, AllQualLists.Count);
    }
}
