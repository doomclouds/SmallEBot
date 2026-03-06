using SmallEBot.Application.Session;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Services.Conversation;

/// <summary>
/// File-based implementation of IConversationRepository.
/// Delegates to ISessionFileService and ISessionManager for file storage.
/// </summary>
public sealed class ConversationRepository(
    ISessionFileService sessionFileService,
    ISessionManager sessionManager) : IConversationRepository
{
    public async Task<ConversationEntity?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(id, ct);
        if (metadata == null || metadata.UserName != userName) return null;
        return ToEntity(metadata);
    }

    public async Task<ConversationEntity?> GetByIdNoUserCheckAsync(Guid id, CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(id, ct);
        return metadata == null ? null : ToEntity(metadata);
    }

    public async Task<List<ConversationEntity>> GetListAsync(string userName, CancellationToken ct = default)
    {
        var summaries = await sessionFileService.ListAsync(userName, ct);
        return summaries.Select(s => new ConversationEntity
        {
            Id = s.Id,
            Title = s.Title,
            UserName = userName,
            UpdatedAt = s.UpdatedAt
        }).ToList();
    }

    public async Task<List<ConversationEntity>> SearchAsync(
        string userName,
        string query,
        bool includeContent = false,
        CancellationToken ct = default)
    {
        var summaries = await sessionFileService.SearchAsync(userName, query, ct);
        return summaries.Select(s => new ConversationEntity
        {
            Id = s.Id,
            Title = s.Title,
            UserName = userName,
            UpdatedAt = s.UpdatedAt
        }).ToList();
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        return metadata?.Turns.Count ?? 0;
    }

    public async Task<ConversationEntity> CreateAsync(string userName, string title, CancellationToken ct = default)
    {
        var metadata = await sessionManager.CreateConversationAsync(userName, title, ct);
        return ToEntity(metadata);
    }

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(id, ct);
        if (metadata == null || metadata.UserName != userName) return false;
        await sessionFileService.DeleteAsync(id, ct);
        return true;
    }

    public async Task<Guid> AddTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        string? newTitle,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null)
            throw new InvalidOperationException($"Conversation {conversationId} not found");

        // Update title if provided (first turn)
        if (!string.IsNullOrEmpty(newTitle))
            metadata.Title = newTitle;

        var turnId = Guid.NewGuid();
        var turn = new TurnMetadata
        {
            Id = turnId,
            CreatedAt = DateTime.UtcNow,
            AttachedPaths = attachedPaths?.ToList() ?? [],
            RequestedSkillIds = requestedSkillIds?.ToList() ?? []
        };
        metadata.Turns.Add(turn);
        metadata.UpdatedAt = DateTime.UtcNow;

        await sessionFileService.SaveAsync(metadata, ct);
        return turnId;
    }

    public Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> segments,
        CancellationToken ct = default)
    {
        // Assistant response is stored in AgentSession, not in metadata
        // This method is kept for interface compatibility but does nothing
        return Task.CompletedTask;
    }

    public Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken ct = default)
    {
        // Error handling is managed by AgentSession
        // This method is kept for interface compatibility but does nothing
        return Task.CompletedTask;
    }

    public async Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null || metadata.UserName != userName) return null;

        var turn = metadata.Turns.FirstOrDefault(t => t.Id == messageId);
        if (turn == null) return null;

        // Update turn metadata with new attachments/skills
        turn.AttachedPaths = attachedPaths?.ToList() ?? [];
        turn.RequestedSkillIds = requestedSkillIds?.ToList() ?? [];
        metadata.UpdatedAt = DateTime.UtcNow;

        await sessionFileService.SaveAsync(metadata, ct);

        // Return empty user message - actual content is in AgentSession
        return (turn.Id, newContent, turn.AttachedPaths, turn.RequestedSkillIds);
    }

    public async Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> GetTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null || metadata.UserName != userName) return null;

        var turn = metadata.Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn == null) return null;

        // Return empty user message - actual content is in AgentSession
        return (turn.Id, "", false, turn.AttachedPaths, turn.RequestedSkillIds);
    }

    public async Task UpdateCompressionAsync(
        Guid conversationId,
        string? compressedContext,
        DateTime? compressedAt,
        CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null) return;

        metadata.CompressedContext = compressedContext;
        metadata.CompressedAt = compressedAt;
        metadata.UpdatedAt = DateTime.UtcNow;

        await sessionFileService.SaveAsync(metadata, ct);
    }

    private static ConversationEntity ToEntity(ConversationMetadata metadata) => new()
    {
        Id = metadata.Id,
        Title = metadata.Title,
        UserName = metadata.UserName,
        CreatedAt = metadata.CreatedAt,
        UpdatedAt = metadata.UpdatedAt,
        CompressedContext = metadata.CompressedContext,
        CompressedAt = metadata.CompressedAt
    };
}
