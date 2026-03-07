namespace SmallEBot.Application.Contracts.UserPreferences;

/// <summary>
/// Application service for user name display with UI state (CurrentDisplayName, UsernameChanged).
/// Stateless IUserPreferencesService is used for persistence; this service adds display state.
/// </summary>
public interface IUserNameDisplayService
{
    /// <summary>Current username for display.</summary>
    string? CurrentDisplayName { get; }

    /// <summary>Raised when CurrentDisplayName is updated.</summary>
    event Action? UsernameChanged;

    /// <summary>Gets username from storage, updates CurrentDisplayName and raises UsernameChanged.</summary>
    Task<string?> GetUserNameAsync(CancellationToken ct = default);

    /// <summary>Updates UserName and persists, updates CurrentDisplayName and raises UsernameChanged.</summary>
    Task SetUserNameAsync(string? userName, CancellationToken ct = default);
}
