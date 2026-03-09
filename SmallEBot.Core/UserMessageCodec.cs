using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmallEBot.Core;

/// <summary>
/// Encodes/decodes attachment metadata (files, skills) in user message text.
/// Format: &lt;!--meta:{"files":["a"],"skills":["b"]}--&gt;\n\nActual message
/// The LLM sees the full text including the meta block; the UI strips it for display.
/// </summary>
public static partial class UserMessageCodec
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    [GeneratedRegex(@"^<!--meta:(.*?)-->\s*", RegexOptions.Singleline)]
    private static partial Regex MetaPattern();

    /// <summary>
    /// Encode attachments into user message text. Returns the original text if no attachments.
    /// </summary>
    public static string Encode(string text, IReadOnlyList<string>? files, IReadOnlyList<string>? skills)
    {
        var hasFiles = files is { Count: > 0 };
        var hasSkills = skills is { Count: > 0 };
        if (!hasFiles && !hasSkills)
            return text;

        var meta = new MetaBlock
        {
            Files = hasFiles ? files! : null,
            Skills = hasSkills ? skills! : null
        };
        var json = JsonSerializer.Serialize(meta, JsonOpts);
        return $"<!--meta:{json}-->\n\n{text}";
    }

    /// <summary>
    /// Decode a user message: extract display text, files, and skills.
    /// </summary>
    public static DecodedMessage Decode(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return new DecodedMessage("", [], []);

        var match = MetaPattern().Match(rawText);
        if (!match.Success)
            return new DecodedMessage(rawText, [], []);

        var json = match.Groups[1].Value;
        var displayText = rawText[match.Length..];

        try
        {
            var meta = JsonSerializer.Deserialize<MetaBlock>(json);
            return new DecodedMessage(
                displayText,
                meta?.Files as IReadOnlyList<string> ?? [],
                meta?.Skills as IReadOnlyList<string> ?? []);
        }
        catch
        {
            return new DecodedMessage(rawText, [], []);
        }
    }

    public record DecodedMessage(string Text, IReadOnlyList<string> Files, IReadOnlyList<string> Skills)
    {
        public bool HasAttachments => Files.Count > 0 || Skills.Count > 0;
    }

    private sealed class MetaBlock
    {
        public IReadOnlyList<string>? Files { get; init; }
        public IReadOnlyList<string>? Skills { get; init; }
    }
}
