using Microsoft.EntityFrameworkCore;
using SmallEBot.Core.Entities;
using SmallEBot.Infrastructure.Data;

namespace SmallEBot.Infrastructure.Data;

/// <summary>One-off backfill of TurnId for existing data. Run once after AddConversationTurns migration.</summary>
public class BackfillTurnsService(SmallEBotDbContext db)
{
    public async Task RunAsync(CancellationToken ct = default)
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
