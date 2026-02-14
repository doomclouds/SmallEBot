using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SmallEBot.Services;

public class UserNameService(ProtectedSessionStorage storage, UserPreferencesService preferences)
{
    private const string Key = "smallebot-username";

    /// <summary>Current username for display (set after load/dialog).</summary>
    public string? CurrentDisplayName { get; set; }

    /// <summary>Raised when CurrentDisplayName is updated (e.g. after dialog or load). Layout can subscribe to refresh AppBar.</summary>
    public event Action? UsernameChanged;

    /// <summary>Get username: session first, then unified preferences file. Only first visit has neither and will show dialog.</summary>
    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await storage.GetAsync<string>(Key);
            var v = r.Success ? r.Value : null;
            if (!string.IsNullOrWhiteSpace(v))
            {
                CurrentDisplayName = v;
                UsernameChanged?.Invoke();
                return v;
            }
            var fromPrefs = await preferences.GetUserNameAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromPrefs))
            {
                CurrentDisplayName = fromPrefs;
                UsernameChanged?.Invoke();
                await storage.SetAsync(Key, fromPrefs);
                return fromPrefs;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Persist to session and to unified preferences file so next time (even new session) no dialog.</summary>
    public async Task SetAsync(string? userName, CancellationToken ct = default)
    {
        var value = userName?.Trim() ?? "";
        if (string.IsNullOrEmpty(value)) return;
        CurrentDisplayName = value;
        UsernameChanged?.Invoke();
        await storage.SetAsync(Key, value);
        await preferences.SetUserNameAsync(value, ct);
    }
}
