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
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .Include(x => x.ToolCalls.OrderBy(t => t.CreatedAt))
            .Include(x => x.ThinkBlocks.OrderBy(b => b.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);

    /// <summary>Returns conversation timeline (messages, tool calls, think blocks) sorted by CreatedAt.</summary>
    public static List<TimelineItem> GetTimeline(Conversation conv)
    {
        var list = conv.Messages.Select(m => new TimelineItem(m, null, null))
            .Concat(conv.ToolCalls.Select(t => new TimelineItem(null, t, null)))
            .Concat(conv.ThinkBlocks.Select(b => new TimelineItem(null, null, b)))
            .OrderBy(x => x.CreatedAt)
            .ToList();
        return list;
    }

    /// <summary>Returns conversation as message groups: one group = one user message or one AI reply (all segments in order).</summary>
    public static List<MessageGroup> GetMessageGroups(Conversation conv)
    {
        var timeline = GetTimeline(conv);
        var groups = new List<MessageGroup>();
        var currentAssistant = new List<TimelineItem>();
        foreach (var item in timeline)
        {
            if (item.Message is { Role: "user" })
            {
                if (currentAssistant.Count > 0)
                {
                    groups.Add(new AssistantMessageGroup(currentAssistant.ToList()));
                    currentAssistant = [];
                }
                groups.Add(new UserMessageGroup(item.Message));
            }
            else
            {
                currentAssistant.Add(item);
            }
        }
        if (currentAssistant.Count > 0)
            groups.Add(new AssistantMessageGroup(currentAssistant.ToList()));
        return groups;
    }

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) return false;
        var c = await db.Conversations
            .AsSplitQuery()
            .Include(x => x.Messages)
            .Include(x => x.ToolCalls)
            .Include(x => x.ThinkBlocks)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        db.ChatMessages.RemoveRange(c.Messages);
        db.ToolCalls.RemoveRange(c.ToolCalls);
        db.ThinkBlocks.RemoveRange(c.ThinkBlocks);
        db.Conversations.Remove(c);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateTitleAsync(Guid id, string userName, string title, CancellationToken ct = default)
    {
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        c.Title = title;
        c.UpdatedAt = DateTime.UtcNow;
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
