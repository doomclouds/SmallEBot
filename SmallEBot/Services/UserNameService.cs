using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SmallEBot.Services;

public class UserNameService
{
    private const string Key = "smallebot-username";
    private readonly ProtectedSessionStorage _storage;

    public UserNameService(ProtectedSessionStorage storage) => _storage = storage;

    /// <summary>Current username for display (set after load/dialog).</summary>
    public string? CurrentDisplayName { get; set; }

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _storage.GetAsync<string>(Key);
            var v = r.Success ? r.Value : null;
            if (v != null) CurrentDisplayName = v;
            return v;
        }
        catch { return null; }
    }

    public async Task SetAsync(string userName, CancellationToken ct = default)
    {
        CurrentDisplayName = userName;
        await _storage.SetAsync(Key, userName);
    }
}
