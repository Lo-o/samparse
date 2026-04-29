using Markdig;

namespace DisplayParagraph.Services;

public sealed class DocsService
{
    private readonly Dictionary<string, RenderedDoc> _byslug;

    public DocsService(ILogger<DocsService> log)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Docs");
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        _byslug = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.md")
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path)!,
                    path => Render(path, pipeline))
            : new();

        log.LogInformation("Loaded {Count} doc(s) from {Dir}", _byslug.Count, dir);
    }

    public RenderedDoc? Get(string slug) => _byslug.GetValueOrDefault(slug);

    private static RenderedDoc Render(string path, MarkdownPipeline pipeline)
    {
        var source = File.ReadAllText(path);
        var doc = Markdown.Parse(source, pipeline);
        var title = doc
            .OfType<Markdig.Syntax.HeadingBlock>()
            .FirstOrDefault(h => h.Level == 1)
            ?.Inline?.FirstChild?.ToString();
        var html = doc.ToHtml(pipeline);
        return new RenderedDoc(title ?? Path.GetFileNameWithoutExtension(path), html);
    }
}

public sealed record RenderedDoc(string Title, string Html);
