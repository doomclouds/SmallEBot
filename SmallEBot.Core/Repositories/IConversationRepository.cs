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
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default);
    Task<Conversation> CreateAsync(string userName, string title, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default);
    Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, CancellationToken ct = default);
    Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct = default);
    Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct = default);
}
