using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SmallEBot.Domain.UserPreferences;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// File-based implementation of IUserPreferenceRepository.
/// Preferences are stored in .agents/settings.json.
/// Thread-safe with ReaderWriterLockSlim and in-memory cache for lazy loading.
/// </summary>
public sealed class UserPreferenceRepository : IUserPreferenceRepository, IDisposable
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ReaderWriterLockSlim _lock = new();
    private UserPreference? _cache;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of UserPreferenceRepository.
    /// </summary>
    /// <param name="basePath">The base path for storing settings (application root directory).</param>
    public UserPreferenceRepository(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
    }

    /// <inheritdoc />
    public async Task<UserPreference> LoadAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _lock.EnterUpgradeableReadLock();
        try
        {
            // First check (under upgradeable read lock)
            if (_cache is not null)
            {
                return _cache;
            }

            // Upgrade to write lock
            _lock.EnterWriteLock();
            try
            {
                // Second check - must re-check after acquiring write lock!
                if (_cache is not null)
                {
                    return _cache;
                }

                var filePath = GetSettingsFilePath();

                if (!File.Exists(filePath))
                {
                    _cache = new UserPreference();
                    return _cache;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                    var dto = JsonSerializer.Deserialize<UserPreferenceDto>(json, _jsonOptions);

                    _cache = dto is not null ? MapToEntity(dto) : new UserPreference();
                    return _cache;
                }
                catch (JsonException)
                {
                    // Handle corrupted JSON gracefully
                    _cache = new UserPreference();
                    return _cache;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(UserPreference preference, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(preference);

        var filePath = GetSettingsFilePath();
        var directoryPath = Path.GetDirectoryName(filePath)!;

        _lock.EnterWriteLock();
        try
        {
            Directory.CreateDirectory(directoryPath);

            var dto = MapToDto(preference);
            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);

            // Update cache after successful save
            _cache = preference;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private string GetSettingsFilePath()
    {
        return Path.Combine(_basePath, ".agents", "settings.json");
    }

    private static UserPreferenceDto MapToDto(UserPreference entity)
    {
        return new UserPreferenceDto
        {
            UserName = entity.UserName,
            Theme = entity.Theme,
            UseThinkingMode = entity.UseThinkingMode,
            ShowToolCalls = entity.ShowToolCalls
        };
    }

    private static UserPreference MapToEntity(UserPreferenceDto dto)
    {
        var entity = new UserPreference();
        entity.SetUserName(dto.UserName);
        entity.SetTheme(dto.Theme);
        entity.SetUseThinkingMode(dto.UseThinkingMode);
        entity.SetShowToolCalls(dto.ShowToolCalls);
        return entity;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UserPreferenceRepository));
        }
    }

    /// <summary>
    /// Releases all resources used by the UserPreferenceRepository.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// DTO for JSON serialization of UserPreference.
    /// </summary>
    private sealed class UserPreferenceDto
    {
        public string? UserName { get; set; }
        public string Theme { get; set; } = UserPreference.DefaultThemeId;
        public bool UseThinkingMode { get; set; } = true;
        public bool ShowToolCalls { get; set; }
    }
}
