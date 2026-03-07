using SmallEBot.Application.Contracts.UserPreferences;
using SmallEBot.Domain.UserPreferences;

namespace SmallEBot.Application.UserPreferences;

/// <summary>
/// Application service for user preferences. Uses IUserPreferenceRepository.
/// </summary>
public sealed class UserPreferencesService(IUserPreferenceRepository repository) : IUserPreferencesService
{
    /// <inheritdoc />
    public async Task<UserPreferencesDto> LoadAsync(CancellationToken ct = default)
    {
        var pref = await repository.LoadAsync(ct);
        return new UserPreferencesDto(
            pref.Theme,
            string.IsNullOrWhiteSpace(pref.UserName) ? null : pref.UserName.Trim(),
            pref.UseThinkingMode,
            pref.ShowToolCalls);
    }

    /// <inheritdoc />
    public async Task SetThemeAsync(string themeId, CancellationToken ct = default)
    {
        var pref = await repository.LoadAsync(ct);
        pref.SetTheme(string.IsNullOrEmpty(themeId) ? UserPreference.DefaultThemeId : themeId);
        await repository.SaveAsync(pref, ct);
    }

    /// <inheritdoc />
    public async Task SetUseThinkingModeAsync(bool value, CancellationToken ct = default)
    {
        var pref = await repository.LoadAsync(ct);
        pref.SetUseThinkingMode(value);
        await repository.SaveAsync(pref, ct);
    }

    /// <inheritdoc />
    public async Task SetShowToolCallsAsync(bool value, CancellationToken ct = default)
    {
        var pref = await repository.LoadAsync(ct);
        pref.SetShowToolCalls(value);
        await repository.SaveAsync(pref, ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetUserNameAsync(CancellationToken ct = default)
    {
        var pref = await repository.LoadAsync(ct);
        return string.IsNullOrWhiteSpace(pref.UserName) ? null : pref.UserName.Trim();
    }

    /// <inheritdoc />
    public async Task SetUserNameAsync(string? userName, CancellationToken ct = default)
    {
        var value = userName?.Trim() ?? "";
        if (string.IsNullOrEmpty(value)) return;
        var pref = await repository.LoadAsync(ct);
        pref.SetUserName(value);
        await repository.SaveAsync(pref, ct);
    }
}
