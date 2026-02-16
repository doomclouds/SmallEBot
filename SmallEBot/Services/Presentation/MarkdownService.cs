using Markdig;

namespace SmallEBot.Services.Presentation;

/// <summary>Renders Markdown to HTML using a pipeline with advanced extensions (tables, footnotes, math, task lists, etc.).</summary>
public class MarkdownService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>Converts Markdown to safe HTML. Returns empty string for null/whitespace.</summary>
    public string ToHtml(string? markdown)
    {
        return string.IsNullOrWhiteSpace(markdown) ? string.Empty : Markdown.ToHtml(markdown, _pipeline);
    }
}
