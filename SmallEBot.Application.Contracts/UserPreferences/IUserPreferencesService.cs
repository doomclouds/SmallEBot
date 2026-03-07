namespace SmallEBot.Application.Contracts.UserPreferences;

/// <summary>
/// Application service for user preferences (theme, username, useThinkingMode, showToolCalls).
/// Stateless; for UI display state use IUserNameDisplayService.
/// </summary>
public interface IUserPreferencesService
{
    /// <summary>Loads preferences from storage.</summary>
    Task<UserPreferencesDto> LoadAsync(CancellationToken ct = default);

    /// <summary>Updates theme and persists.</summary>
    Task SetThemeAsync(string themeId, CancellationToken ct = default);

    /// <summary>Updates UseThinkingMode and persists.</summary>
    Task SetUseThinkingModeAsync(bool value, CancellationToken ct = default);

    /// <summary>Updates ShowToolCalls and persists.</summary>
    Task SetShowToolCallsAsync(bool value, CancellationToken ct = default);

    /// <summary>Gets username from persisted storage.</summary>
    Task<string?> GetUserNameAsync(CancellationToken ct = default);

    /// <summary>Updates UserName and persists.</summary>
    Task SetUserNameAsync(string? userName, CancellationToken ct = default);
}

/// <summary>DTO for user preferences.</summary>
public sealed record UserPreferencesDto(
    string Theme,
    string? UserName,
    bool UseThinkingMode,
    bool ShowToolCalls);
