using Microsoft.EntityFrameworkCore;
using SmallEBot.Data;
using SmallEBot.Data.Entities;
using SmallEBot.Models;

namespace SmallEBot.Services;

public class ConversationService(AppDbContext db)
{
    public async Task<Conversation> CreateAsync(string userName, CancellationToken ct = default)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Title = "新对话",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Conversations.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default) =>
        await db.Conversations
            .Where(x => x.UserName == userName)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await db.Conversations
            .AsSplitQuery()
            .Include(c => c.Turns.OrderBy(t => t.CreatedAt))
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .Include(x => x.ToolCalls.OrderBy(t => t.CreatedAt))
            .Include(x => x.ThinkBlocks.OrderBy(b => b.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);

    /// <summary>Returns conversation timeline (messages, tool calls, think blocks) sorted by CreatedAt.</summary>
    private static List<TimelineItem> GetTimeline(IEnumerable<ChatMessage> messages, IEnumerable<ToolCall> toolCalls, IEnumerable<ThinkBlock> thinkBlocks)
    {
        var list = messages.Select(m => new TimelineItem(m, null, null))
            .Concat(toolCalls.Select(t => new TimelineItem(null, t, null)))
            .Concat(thinkBlocks.Select(b => new TimelineItem(null, null, b)))
            .OrderBy(x => x.CreatedAt)
            .ToList();
        return list;
    }

    /// <summary>Returns conversation as chat bubbles from turns: one bubble = one user message or one assistant reply (all segments in order).</summary>
    public static List<ChatBubble> GetChatBubbles(Conversation conv)
    {
        var bubbles = new List<ChatBubble>();
        var turns = conv.Turns.OrderBy(t => t.CreatedAt).ToList();
        if (turns.Count == 0)
        {
            // Legacy: no turns, fall back to timeline-based bubble building
            var timeline = GetTimeline(conv.Messages, conv.ToolCalls, conv.ThinkBlocks);
            var currentAssistant = new List<TimelineItem>();
            foreach (var item in timeline)
            {
                if (item.Message is { Role: "user" })
                {
                    if (currentAssistant.Count > 0)
                        bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false));
                    bubbles.Add(new UserBubble(item.Message));
                    currentAssistant = [];
                }
                else
                    currentAssistant.Add(item);
            }
            if (currentAssistant.Count > 0)
                bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false));
            return bubbles;
        }

        foreach (var turn in turns)
        {
            var userMsg = conv.Messages.FirstOrDefault(m => m.TurnId == turn.Id && m.Role == "user");
            if (userMsg == null) continue;

            var turnMessages = conv.Messages.Where(m => m.TurnId == turn.Id && m.Role == "assistant").ToList();
            var turnTools = conv.ToolCalls.Where(t => t.TurnId == turn.Id).ToList();
            var turnThinks = conv.ThinkBlocks.Where(b => b.TurnId == turn.Id).ToList();
            var items = GetTimeline(turnMessages, turnTools, turnThinks);

            bubbles.Add(new UserBubble(userMsg));
            bubbles.Add(new AssistantBubble(items, turn.IsThinkingMode));
        }
        return bubbles;
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

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ChatMessages.CountAsync(x => x.ConversationId == conversationId, ct);

    /// <summary>Backfill TurnId for existing data. Run once after AddConversationTurns migration.</summary>
    public async Task BackfillTurnsAsync(CancellationToken ct = default)
    {
        if (!await db.ChatMessages.AnyAsync(m => m.TurnId == null, ct))
            return;

        var conversations = await db.Conversations
            .AsSplitQuery()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .Include(c => c.ToolCalls.OrderBy(t => t.CreatedAt))
            .Include(c => c.ThinkBlocks.OrderBy(b => b.CreatedAt))
            .ToListAsync(ct);

        foreach (var conv in conversations)
        {
            var userMessages = conv.Messages.Where(m => m.Role == "user").OrderBy(m => m.CreatedAt).ToList();
            for (var i = 0; i < userMessages.Count; i++)
            {
                var userMsg = userMessages[i];
                var rangeStart = userMsg.CreatedAt;
                var rangeEnd = i + 1 < userMessages.Count
                    ? userMessages[i + 1].CreatedAt
                    : DateTime.MaxValue;

                var turn = new ConversationTurn
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conv.Id,
                    IsThinkingMode = conv.ThinkBlocks.Any(b => b.CreatedAt >= rangeStart && b.CreatedAt < rangeEnd),
                    CreatedAt = userMsg.CreatedAt
                };
                db.ConversationTurns.Add(turn);

                userMsg.TurnId = turn.Id;
                foreach (var m in conv.Messages.Where(m => m.CreatedAt >= rangeStart && m.CreatedAt < rangeEnd))
                    m.TurnId = turn.Id;
                foreach (var t in conv.ToolCalls.Where(t => t.CreatedAt >= rangeStart && t.CreatedAt < rangeEnd))
                    t.TurnId = turn.Id;
                foreach (var b in conv.ThinkBlocks.Where(b => b.CreatedAt >= rangeStart && b.CreatedAt < rangeEnd))
                    b.TurnId = turn.Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
