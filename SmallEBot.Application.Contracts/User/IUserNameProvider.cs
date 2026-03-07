namespace SmallEBot.Application.Contracts.User;

/// <summary>
/// Provides the current user name for the application.
/// Username is persisted in session storage and unified preferences file.
/// </summary>
public interface IUserNameProvider
{
    /// <summary>
    /// Current username for display.
    /// </summary>
    string? CurrentDisplayName { get; }

    /// <summary>
    /// Raised when CurrentDisplayName is updated (e.g. after dialog or load).
    /// </summary>
    event Action? UsernameChanged;

    /// <summary>
    /// Gets the username: session first, then unified preferences file.
    /// Returns null if neither is set (first visit).
    /// </summary>
    Task<string?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the username to session and unified preferences file.
    /// </summary>
    Task SetAsync(string? userName, CancellationToken ct = default);
}
