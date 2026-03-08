using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Infrastructure.Conversations.Metadata;

/// <summary>
/// File-based implementation of IConversationMetadataRepository.
/// Metadata is stored in .agents/conversations/{conversationId:N}/metadata.json
/// Thread-safe with SemaphoreSlim for async-safe locking.
/// </summary>
public sealed class ConversationMetadataRepository : IConversationMetadataRepository, IDisposable
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of ConversationMetadataRepository.
    /// </summary>
    /// <param name="basePath">The base path for storing conversation data (application root directory).</param>
    public ConversationMetadataRepository(string basePath)
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
    public async Task<ConversationMetadata?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetMetadataFilePath(id);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ConversationMetadataPersistence>(json, _jsonOptions);
            return dto is not null ? FromPersistence(dto) : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationMetadata>> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(userName);

        var allMetadata = await LoadAllAsync(ct).ConfigureAwait(false);
        return allMetadata
            .Where(m => m.UserName == userName)
            .OrderByDescending(m => m.UpdatedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationMetadata>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(query);

        var allMetadata = await LoadAllAsync(ct).ConfigureAwait(false);
        return allMetadata
            .Where(m => m.UserName == userName &&
                        m.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.UpdatedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(metadata);

        var directoryPath = GetConversationDirectory(metadata.Id);
        var filePath = GetMetadataFilePath(metadata.Id);
        var dto = ToPersistence(metadata);
        var json = JsonSerializer.Serialize(dto, _jsonOptions);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var directoryPath = GetConversationDirectory(id);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetTurnCountAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var metadata = await GetByIdAsync(conversationId, ct).ConfigureAwait(false);
        return metadata?.Turns.Count ?? 0;
    }

    /// <summary>
    /// Loads all conversation metadata from storage.
    /// Handles corrupt files gracefully by skipping them.
    /// </summary>
    private async Task<IReadOnlyList<ConversationMetadata>> LoadAllAsync(CancellationToken ct = default)
    {
        var conversationsBasePath = Path.Combine(_basePath, ".agents", "conversations");

        string[] filesToRead;
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(conversationsBasePath))
            {
                return Array.Empty<ConversationMetadata>();
            }
            var conversationDirs = Directory.GetDirectories(conversationsBasePath);
            filesToRead = conversationDirs
                .Select(dir => Path.Combine(dir, "metadata.json"))
                .Where(File.Exists)
                .ToArray();
        }
        finally
        {
            _semaphore.Release();
        }

        var results = new List<ConversationMetadata>();
        foreach (var filePath in filesToRead)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                var dto = JsonSerializer.Deserialize<ConversationMetadataPersistence>(json, _jsonOptions);
                if (dto is not null)
                {
                    results.Add(FromPersistence(dto));
                }
            }
            catch (JsonException)
            {
                // Skip corrupt files
            }
            catch (IOException)
            {
                // Skip files that can't be read
            }
        }

        return results;
    }

    private static ConversationMetadataPersistence ToPersistence(ConversationMetadata m)
    {
        return new ConversationMetadataPersistence
        {
            Id = m.Id,
            Title = m.Title,
            UserName = m.UserName,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            CompressedContext = m.CompressedContext,
            CompressedAt = m.CompressedAt,
            Turns = m.Turns.Select(t => new TurnInfoPersistence
            {
                Id = t.Id,
                CreatedAt = t.CreatedAt,
                FirstMessageIndex = t.FirstMessageIndex,
                AttachedPaths = t.AttachedPaths.ToList(),
                RequestedSkillIds = t.RequestedSkillIds.ToList()
            }).ToList()
        };
    }

    private static ConversationMetadata FromPersistence(ConversationMetadataPersistence dto)
    {
        var metadata = new ConversationMetadata(
            dto.Id,
            dto.Title,
            dto.UserName,
            dto.CreatedAt);

        foreach (var t in dto.Turns)
        {
            var turn = new TurnInfo(
                t.Id,
                t.CreatedAt,
                t.FirstMessageIndex,
                t.AttachedPaths.ToArray(),
                t.RequestedSkillIds.ToArray());
            metadata.AddExistingTurn(turn);
        }

        metadata.SetUpdatedAt(dto.UpdatedAt);
        metadata.SetCompressedContextForLoad(dto.CompressedContext, dto.CompressedAt);
        return metadata;
    }

    private string GetConversationDirectory(Guid conversationId)
    {
        return Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"));
    }

    private string GetMetadataFilePath(Guid conversationId)
    {
        return Path.Combine(GetConversationDirectory(conversationId), "metadata.json");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConversationMetadataRepository));
        }
    }

    /// <summary>
    /// Releases all resources used by the ConversationMetadataRepository.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Dispose();
        _disposed = true;
    }
}
