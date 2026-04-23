using Samparse;

namespace DisplayParagraph.Services;

public class SamDataService
{
    private readonly string _dataPath;
    private SamDatabase? _database;
    private ChapterIvIndex? _chapterIvIndex;
    private QualificationListResolver? _qualResolver;

    public SamDataService(IConfiguration config, IWebHostEnvironment env)
    {
        var relPath = config["SamDataPath"] ?? "../../trial/SAM";
        _dataPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, relPath));
    }

    public SamDatabase Database => _database ??= SamLoader.Load(_dataPath);

    public ChapterIvIndex ChapterIvIndex => _chapterIvIndex ??= new ChapterIvIndex(Database.ChapterIv!);

    public QualificationListResolver QualResolver =>
        _qualResolver ??= new QualificationListResolver(Database.ChapterIv!, Database.References!);
}
