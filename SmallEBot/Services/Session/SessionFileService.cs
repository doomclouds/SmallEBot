using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Session;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

public sealed class SessionFileService(ILogger<SessionFileService> logger) : ISessionFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Allow Chinese and other Unicode chars
    };

    // Sessions stored in .agents/sessions/
    private readonly string _sessionsDir = Path.Combine(AppContext.BaseDirectory, ".agents", "sessions");

    public string SessionsDirectory
    {
        get
        {
            Directory.CreateDirectory(_sessionsDir);
            return _sessionsDir;
        }
    }

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

        foreach (var file in Directory.GetFiles(SessionsDirectory, "*.json"))
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
            catch (Exception ex)
            {
                // Skip malformed or corrupted files, but log for debugging
                logger.LogWarning(ex, "Failed to parse session file: {FilePath}", file);
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
