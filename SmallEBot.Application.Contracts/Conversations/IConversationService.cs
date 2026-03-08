using ChatBubble = SmallEBot.Core.Models.ChatBubble;

namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Orchestrates conversation CRUD and turn management. Implemented in Application; consumed by Host.</summary>
/// <remarks>Agent execution (streaming, compression, session truncation) is handled by IConversationAgentDispatcher.</remarks>
public interface IConversationService
{
    Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default);
    Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    /// <summary>Search conversations by title. Returns GetConversationsAsync when query is empty.</summary>
    Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default);
    Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Get chat bubbles from a conversation's AgentSession.</summary>
    Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Creates a turn and user message; returns turn id. Call before StreamResponseAndCompleteAsync.</summary>
    /// <param name="suggestedTitle">When this is the first message, use this as the conversation title (e.g. from LLM). If null, a simple default is used.</param>
    Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        string? suggestedTitle = null);

    /// <summary>Replace user message with new content, delete subsequent turns, create new turn. Call before streaming. Returns (turnId, userMessage, attachedPaths, requestedSkillIds) or null.</summary>
    Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken cancellationToken = default);

}
