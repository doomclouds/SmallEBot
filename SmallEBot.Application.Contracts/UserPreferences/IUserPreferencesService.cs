namespace SmallEBot.Application.Contracts.UserPreferences;

/// <summary>
/// Application service for user preferences (theme, username, useThinkingMode, showToolCalls).
/// </summary>
public interface IUserPreferencesService
{
    /// <summary>Current username for display. Updated by GetUserNameAsync and SetUserNameAsync.</summary>
    string? CurrentDisplayName { get; }

    /// <summary>Raised when CurrentDisplayName is updated (e.g. after load or set).</summary>
    event Action? UsernameChanged;

    /// <summary>Loads preferences from storage.</summary>
    Task<UserPreferencesDto> LoadAsync(CancellationToken ct = default);

    /// <summary>Updates theme and persists.</summary>
    Task SetThemeAsync(string themeId, CancellationToken ct = default);

    /// <summary>Updates UseThinkingMode and persists.</summary>
    Task SetUseThinkingModeAsync(bool value, CancellationToken ct = default);

    /// <summary>Updates ShowToolCalls and persists.</summary>
    Task SetShowToolCallsAsync(bool value, CancellationToken ct = default);

    /// <summary>Gets username from storage, updates CurrentDisplayName and raises UsernameChanged.</summary>
    Task<string?> GetUserNameAsync(CancellationToken ct = default);

    /// <summary>Updates UserName and persists, updates CurrentDisplayName and raises UsernameChanged.</summary>
    Task SetUserNameAsync(string? userName, CancellationToken ct = default);
}

/// <summary>DTO for user preferences.</summary>
public sealed record UserPreferencesDto(
    string Theme,
    string? UserName,
    bool UseThinkingMode,
    bool ShowToolCalls);
