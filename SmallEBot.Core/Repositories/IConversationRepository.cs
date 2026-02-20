using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default);
    Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default);
    Task<List<Conversation>> SearchAsync(
        string userName,
        string query,
        bool includeContent = false,
        CancellationToken ct = default);
    Task<List<ChatMessage>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<List<ToolCall>> GetToolCallsForConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<List<ThinkBlock>> GetThinkBlocksForConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default);
    Task<Conversation> CreateAsync(string userName, string title, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default);
    Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, IReadOnlyList<string>? attachedPaths = null, IReadOnlyList<string>? requestedSkillIds = null, CancellationToken ct = default);
    Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct = default);
    Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct = default);

    /// <summary>Replace user message with new content, mark original as replaced, delete assistant and subsequent turns, create new turn. Returns (turnId, userMessage, attachedPaths, requestedSkillIds) for streaming.</summary>
    Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken ct = default);

    /// <summary>Delete assistant content of turn and all subsequent turns; return user message for regenerate. Returns null if not found.</summary>
    Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> GetTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken ct = default);
}
