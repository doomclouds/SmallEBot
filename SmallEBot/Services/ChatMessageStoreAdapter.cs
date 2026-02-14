using Microsoft.EntityFrameworkCore;
using SmallEBot.Data;

namespace SmallEBot.Services;

/// <summary>
/// Adapts EF Core ChatMessage entities to Agent Framework's history.
/// Load messages from DB, pass to agent via AgentThread when wiring in Task 13.
/// </summary>
public class ChatMessageStoreAdapter(AppDbContext db, Guid conversationId)
{
    /// <summary>
    /// Load chat messages for the conversation, ordered by CreatedAt.
    /// Used when building agent input with persisted history (Task 13).
    /// </summary>
    public async Task<List<Data.Entities.ChatMessage>> LoadMessagesAsync(CancellationToken ct = default) =>
        await db.ChatMessages
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
}
