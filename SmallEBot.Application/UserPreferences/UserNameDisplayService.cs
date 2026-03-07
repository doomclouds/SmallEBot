using SmallEBot.Application.Contracts.UserPreferences;

namespace SmallEBot.Application.UserPreferences;

/// <summary>
/// Adds UI display state (CurrentDisplayName, UsernameChanged) on top of IUserPreferencesService.
/// </summary>
public sealed class UserNameDisplayService(IUserPreferencesService preferences) : IUserNameDisplayService
{
    /// <inheritdoc />
    public string? CurrentDisplayName { get; set; }

    /// <inheritdoc />
    public event Action? UsernameChanged;

    /// <inheritdoc />
    public async Task<string?> GetUserNameAsync(CancellationToken ct = default)
    {
        var name = await preferences.GetUserNameAsync(ct);
        if (name != CurrentDisplayName)
        {
            CurrentDisplayName = name;
            UsernameChanged?.Invoke();
        }
        return name;
    }

    /// <inheritdoc />
    public async Task SetUserNameAsync(string? userName, CancellationToken ct = default)
    {
        var value = userName?.Trim() ?? "";
        if (string.IsNullOrEmpty(value)) return;
        await preferences.SetUserNameAsync(value, ct);
        CurrentDisplayName = value;
        UsernameChanged?.Invoke();
    }
}
