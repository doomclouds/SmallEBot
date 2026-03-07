// SmallEBot.Domain/UserPreferences/IUserPreferenceRepository.cs
namespace SmallEBot.Domain.UserPreferences;

/// <summary>
/// Repository interface for user preferences.
/// </summary>
public interface IUserPreferenceRepository
{
    /// <summary>
    /// Loads user preferences.
    /// </summary>
    Task<UserPreference> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves user preferences.
    /// </summary>
    Task SaveAsync(UserPreference preference, CancellationToken ct = default);
}
