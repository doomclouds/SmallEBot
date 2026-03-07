using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SmallEBot.Domain.UserPreferences;

namespace SmallEBot.Infrastructure.UserPreferences;

/// <summary>
/// File-based implementation of IUserPreferenceRepository.
/// Preferences are stored in .agents/settings.json.
/// Thread-safe with SemaphoreSlim for async-safe locking.
/// Migrates from legacy smallebot-settings.json and smallebot-username.txt on first load.
/// </summary>
public sealed class UserPreferenceRepository(string basePath) : IUserPreferenceRepository, IDisposable
{
    private readonly string _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private UserPreference? _cache;
    private bool _disposed;

    /// <inheritdoc />
    public async Task<UserPreference> LoadAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
                return _cache;

            var filePath = GetSettingsFilePath();

            if (!File.Exists(filePath))
            {
                _cache = await MigrateFromLegacyAsync(ct).ConfigureAwait(false);
                if (_cache is not null)
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    var dto = MapToDto(_cache);
                    var json = JsonSerializer.Serialize(dto, _jsonOptions);
                    await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
                }
                else
                {
                    _cache = new UserPreference();
                }
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
                _cache = new UserPreference();
                return _cache;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(UserPreference preference, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(preference);

        var filePath = GetSettingsFilePath();
        var directoryPath = Path.GetDirectoryName(filePath)!;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directoryPath);
            var dto = MapToDto(preference);
            var json = JsonSerializer.Serialize(dto, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
            _cache = preference;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string GetSettingsFilePath() => Path.Combine(_basePath, ".agents", "settings.json");

    private const string LegacySettingsFileName = "smallebot-settings.json";
    private const string LegacyUserNameFileName = "smallebot-username.txt";

    private async Task<UserPreference?> MigrateFromLegacyAsync(CancellationToken ct)
    {
        var legacyPath = Path.Combine(_basePath, LegacySettingsFileName);
        var legacyUserNamePath = Path.Combine(_basePath, LegacyUserNameFileName);

        UserPreference? migrated = null;
        if (File.Exists(legacyPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(legacyPath, ct).ConfigureAwait(false);
                var legacy = JsonSerializer.Deserialize<LegacySettingsDto>(json, _jsonOptions);
                if (legacy is not null)
                {
                    migrated = new UserPreference();
                    migrated.SetTheme(string.IsNullOrEmpty(legacy.Theme) ? UserPreference.DefaultThemeId : legacy.Theme);
                    migrated.SetUserName(legacy.UserName);
                    migrated.SetUseThinkingMode(legacy.UseThinkingMode);
                    migrated.SetShowToolCalls(legacy.ShowToolCalls);
                }
            }
            catch
            {
                /* fall through */
            }
        }

        migrated ??= new UserPreference();

        if (string.IsNullOrWhiteSpace(migrated.UserName) && File.Exists(legacyUserNamePath))
        {
            try
            {
                var legacy = (await File.ReadAllTextAsync(legacyUserNamePath, ct).ConfigureAwait(false)).Trim();
                if (!string.IsNullOrEmpty(legacy))
                    migrated.SetUserName(legacy);
            }
            catch
            {
                /* ignore */
            }
        }

        return migrated;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UserPreferenceRepository));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _semaphore.Dispose();
        _disposed = true;
    }

    private static UserPreferenceDto MapToDto(UserPreference entity) => new()
    {
        UserName = entity.UserName,
        Theme = entity.Theme,
        UseThinkingMode = entity.UseThinkingMode,
        ShowToolCalls = entity.ShowToolCalls
    };

    private static UserPreference MapToEntity(UserPreferenceDto dto)
    {
        var entity = new UserPreference();
        entity.SetUserName(dto.UserName);
        entity.SetTheme(dto.Theme);
        entity.SetUseThinkingMode(dto.UseThinkingMode);
        entity.SetShowToolCalls(dto.ShowToolCalls);
        return entity;
    }

    private sealed class LegacySettingsDto
    {
        public string? Theme { get; set; }
        public string? UserName { get; set; }
        public bool UseThinkingMode { get; set; }
        public bool ShowToolCalls { get; set; } = true;
    }

    private sealed class UserPreferenceDto
    {
        public string? UserName { get; set; }
        public string Theme { get; set; } = UserPreference.DefaultThemeId;
        public bool UseThinkingMode { get; set; } = true;
        public bool ShowToolCalls { get; set; } = true;
    }
}
