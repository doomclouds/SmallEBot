namespace SmallEBot.Models;

/// <summary>
/// User preferences persisted in a single JSON file (theme, username, useThinkingMode, showToolCalls).
/// </summary>
public sealed class SmallEBotSettings
{
    public const string DefaultThemeId = "editorial-dark";

    public string Theme { get; set; } = DefaultThemeId;
    public string UserName { get; set; } = "";
    public bool UseThinkingMode { get; set; }
    public bool ShowToolCalls { get; set; } = true;
    public List<string> DisabledMcpIds { get; set; } = [];
}
