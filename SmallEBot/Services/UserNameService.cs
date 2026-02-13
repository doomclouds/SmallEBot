using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SmallEBot.Services;

public class UserNameService
{
    private const string Key = "smallebot-username";
    private readonly ProtectedSessionStorage _storage;

    public UserNameService(ProtectedSessionStorage storage) => _storage = storage;

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _storage.GetAsync<string>(Key);
            return r.Success ? r.Value : null;
        }
        catch { return null; }
    }

    public async Task SetAsync(string userName, CancellationToken ct = default) =>
        await _storage.SetAsync(Key, userName);
}
