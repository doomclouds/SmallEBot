using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SmallEBot.Services;

public class UserNameService(ProtectedSessionStorage storage, IWebHostEnvironment env)
{
    private const string Key = "smallebot-username";
    private const string FileName = "smallebot-username.txt";
    private readonly string _filePath = Path.Combine(env.ContentRootPath, FileName);

    /// <summary>Current username for display (set after load/dialog).</summary>
    public string? CurrentDisplayName { get; set; }

    /// <summary>Get username: session first, then file. Only first visit has neither and will show dialog.</summary>
    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await storage.GetAsync<string>(Key);
            var v = r.Success ? r.Value : null;
            if (!string.IsNullOrWhiteSpace(v))
            {
                CurrentDisplayName = v;
                return v;
            }
            var fromFile = await ReadFromFileAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                CurrentDisplayName = fromFile;
                await storage.SetAsync(Key, fromFile);
                return fromFile;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Persist to session and to file so next time (even new session) no dialog.</summary>
    public async Task SetAsync(string? userName, CancellationToken ct = default)
    {
        var value = userName?.Trim() ?? "";
        if (string.IsNullOrEmpty(value)) return;
        CurrentDisplayName = value;
        await storage.SetAsync(Key, value);
        await WriteToFileAsync(value, ct);
    }

    private async Task<string?> ReadFromFileAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            var text = await File.ReadAllTextAsync(_filePath, ct);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch { return null; }
    }

    private async Task WriteToFileAsync(string value, CancellationToken ct)
    {
        try { await File.WriteAllTextAsync(_filePath, value, ct); }
        catch { /* ignore */ }
    }
}
