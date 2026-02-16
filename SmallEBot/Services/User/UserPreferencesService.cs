using System.Text.Json;
using SmallEBot.Models;

namespace SmallEBot.Services.User;

/// <summary>
/// Persists and loads theme, username, useThinkingMode, and showToolCalls in a single JSON file.
/// Path: AppDomain.CurrentDomain.BaseDirectory (same as DB and MCP config).
/// </summary>
public class UserPreferencesService
{
    private const string FileName = "smallebot-settings.json";
    private const string LegacyUserNameFileName = "smallebot-username.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string BasePath => AppDomain.CurrentDomain.BaseDirectory;
    private readonly string _filePath = Path.Combine(BasePath, FileName);
    private readonly string _legacyUserNamePath = Path.Combine(BasePath, LegacyUserNameFileName);
    private SmallEBotSettings? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Load preferences from file (with migration from legacy username file if needed).</summary>
    public async Task<SmallEBotSettings> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null)
                return _cached;

            SmallEBotSettings? loaded = null;
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_filePath, ct);
                    loaded = JsonSerializer.Deserialize<SmallEBotSettings>(json);
                }
                catch
                {
                    /* use defaults */
                }
            }

            loaded ??= new SmallEBotSettings();

            // Migrate from legacy smallebot-username.txt if new file had no username
            if (string.IsNullOrWhiteSpace(loaded.UserName) && File.Exists(_legacyUserNamePath))
            {
                try
                {
                    var legacy = (await File.ReadAllTextAsync(_legacyUserNamePath, ct)).Trim();
                    if (!string.IsNullOrEmpty(legacy))
                    {
                        loaded.UserName = legacy;
                        await SaveInternalAsync(loaded, ct);
                    }
                }
                catch
                {
                    /* ignore */
                }
            }

            _cached = loaded;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Update only theme and persist.</summary>
    public async Task SetThemeAsync(string themeId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = _cached ?? await LoadInternalAsync(ct);
            _cached ??= current;
            current.Theme = string.IsNullOrEmpty(themeId) ? SmallEBotSettings.DefaultThemeId : themeId;
            await SaveInternalAsync(current, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Update only UseThinkingMode and persist.</summary>
    public async Task SetUseThinkingModeAsync(bool value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = _cached ?? await LoadInternalAsync(ct);
            _cached ??= current;
            current.UseThinkingMode = value;
            await SaveInternalAsync(current, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Update only ShowToolCalls and persist.</summary>
    public async Task SetShowToolCallsAsync(bool value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = _cached ?? await LoadInternalAsync(ct);
            _cached ??= current;
            current.ShowToolCalls = value;
            await SaveInternalAsync(current, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Get current username from persisted file (no session).</summary>
    public async Task<string?> GetUserNameAsync(CancellationToken ct = default)
    {
        var prefs = await LoadAsync(ct);
        return string.IsNullOrWhiteSpace(prefs.UserName) ? null : prefs.UserName.Trim();
    }

    /// <summary>Update only UserName and persist.</summary>
    public async Task SetUserNameAsync(string? userName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = _cached ?? await LoadInternalAsync(ct);
            _cached ??= current;
            current.UserName = userName?.Trim() ?? "";
            await SaveInternalAsync(current, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SmallEBotSettings> LoadInternalAsync(CancellationToken ct)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                var o = JsonSerializer.Deserialize<SmallEBotSettings>(json);
                if (o != null) return o;
            }
            catch
            {
                /* fall through to default */
            }
        }
        return new SmallEBotSettings();
    }

    private async Task SaveInternalAsync(SmallEBotSettings prefs, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(prefs, JsonOptions), ct);
    }
}
