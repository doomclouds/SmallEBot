using Microsoft.EntityFrameworkCore;
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using SmallEBot.Infrastructure.Data;

namespace SmallEBot.Infrastructure.Repositories;

public class ConversationRepository(SmallEBotDbContext db) : IConversationRepository
{
    public async Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await db.Conversations
            .AsSplitQuery()
            .Include(c => c.Turns.OrderBy(t => t.CreatedAt))
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .Include(x => x.ToolCalls.OrderBy(t => t.CreatedAt))
            .Include(x => x.ThinkBlocks.OrderBy(b => b.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);

    public async Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default) =>
        await db.Conversations
            .Where(x => x.UserName == userName)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

    public async Task<List<ChatMessage>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ChatMessages
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ChatMessages.CountAsync(x => x.ConversationId == conversationId, ct);

    public async Task<Conversation> CreateAsync(string userName, string title, CancellationToken ct = default)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Conversations.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) return false;
        var c = await db.Conversations
            .AsSplitQuery()
            .Include(c => c.Turns)
            .Include(x => x.Messages)
            .Include(x => x.ToolCalls)
            .Include(x => x.ThinkBlocks)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        db.ChatMessages.RemoveRange(c.Messages);
        db.ToolCalls.RemoveRange(c.ToolCalls);
        db.ThinkBlocks.RemoveRange(c.ThinkBlocks);
        db.ConversationTurns.RemoveRange(c.Turns);
        db.Conversations.Remove(c);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null)
            throw new InvalidOperationException("Conversation not found.");

        var baseTime = DateTime.UtcNow;
        var turn = new ConversationTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            IsThinkingMode = useThinking,
            CreatedAt = baseTime
        };
        db.ConversationTurns.Add(turn);
        baseTime = baseTime.AddMilliseconds(1);

        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnId = turn.Id,
            Role = "user",
            Content = userMessage,
            CreatedAt = baseTime
        });

        conv.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(newTitle))
            conv.Title = newTitle;

        await db.SaveChangesAsync(ct);
        return turn.Id;
    }

    public async Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct = default)
    {
        var conv = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conv == null) return;

        var baseTime = DateTime.UtcNow;
        var toolOrder = 0;
        var thinkOrder = 0;

        foreach (var seg in segments)
        {
            if (seg.IsText && !string.IsNullOrEmpty(seg.Text))
            {
                db.ChatMessages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    TurnId = turnId,
                    Role = "assistant",
                    Content = seg.Text,
                    CreatedAt = baseTime
                });
            }
            else if (seg.IsThink && !string.IsNullOrEmpty(seg.Text))
            {
                db.ThinkBlocks.Add(new ThinkBlock
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    TurnId = turnId,
                    Content = seg.Text,
                    SortOrder = thinkOrder++,
                    CreatedAt = baseTime
                });
            }
            else if (seg is { IsText: false, IsThink: false })
            {
                db.ToolCalls.Add(new ToolCall
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    TurnId = turnId,
                    ToolName = seg.ToolName ?? "",
                    Arguments = seg.Arguments,
                    Result = seg.Result,
                    SortOrder = toolOrder++,
                    CreatedAt = baseTime
                });
            }
            baseTime = baseTime.AddMilliseconds(1);
        }

        conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct = default)
    {
        var conv = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conv == null) return;

        var content = "Error: " + (string.IsNullOrEmpty(errorMessage) ? "Unknown error" : errorMessage);
        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnId = turnId,
            Role = "assistant",
            Content = content,
            CreatedAt = DateTime.UtcNow
        });

        conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
