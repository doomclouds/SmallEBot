// SmallEBot.Domain/UserPreferences/UserPreference.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.UserPreferences;

/// <summary>
/// Aggregate root for user preferences.
/// </summary>
public class UserPreference : IAggregateRoot
{
    public string? UserName { get; private set; }
    public string Theme { get; private set; } = DefaultThemeId;
    public bool UseThinkingMode { get; private set; } = true;
    public bool ShowToolCalls { get; private set; } = false;

    public const string DefaultThemeId = "light";

    /// <summary>
    /// Sets the theme.
    /// </summary>
    public void SetTheme(string themeId)
    {
        Theme = string.IsNullOrEmpty(themeId) ? DefaultThemeId : themeId;
    }

    /// <summary>
    /// Sets the user name.
    /// </summary>
    public void SetUserName(string? userName)
    {
        UserName = userName?.Trim();
    }

    /// <summary>
    /// Sets whether thinking mode is enabled.
    /// </summary>
    public void SetUseThinkingMode(bool value)
    {
        UseThinkingMode = value;
    }

    /// <summary>
    /// Sets whether tool calls are shown.
    /// </summary>
    public void SetShowToolCalls(bool value)
    {
        ShowToolCalls = value;
    }
}
