using System.Diagnostics;
using Samparse;

namespace DisplayParagraph.Services;

public class SamDataService
{
    public SamDatabase Database { get; }
    public ChapterIvIndex ChapterIvIndex { get; }
    public QualificationListResolver QualResolver { get; }

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

        logger.LogInformation("SAM data loaded in {Elapsed:N1}s", sw.Elapsed.TotalSeconds);
    }
}
