using System.Text.Json;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

public sealed class SessionFileService : ISessionFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _sessionsDir;

    public SessionFileService()
    {
        // Sessions stored in .agents/sessions/
        var appDir = AppContext.BaseDirectory;
        _sessionsDir = Path.Combine(appDir, ".agents", "sessions");
        Directory.CreateDirectory(_sessionsDir);
    }

    public string SessionsDirectory => _sessionsDir;

    public async Task<ConversationMetadata?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ConversationMetadata>(json, JsonOptions);
    }

    public async Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default)
    {
        metadata.UpdatedAt = DateTime.UtcNow;
        var path = GetFilePath(metadata.Id);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(string userName, CancellationToken ct = default)
    {
        var summaries = new List<ConversationSummary>();

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var meta = JsonSerializer.Deserialize<ConversationMetadata>(json, JsonOptions);
                if (meta != null && meta.UserName == userName)
                {
                    summaries.Add(new ConversationSummary
                    {
                        Id = meta.Id,
                        Title = meta.Title,
                        UpdatedAt = meta.UpdatedAt
                    });
                }
            }
            catch
            {
                // Skip malformed files
            }
        }

        return summaries.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<ConversationSummary>> SearchAsync(string userName, string query, CancellationToken ct = default)
    {
        var all = await ListAsync(userName, ct);
        if (string.IsNullOrWhiteSpace(query)) return all;

        return all
            .Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string GetFilePath(Guid id) => Path.Combine(_sessionsDir, $"{id}.json");
}
