using Microsoft.EntityFrameworkCore;
using SmallEBot.Data;
using SmallEBot.Data.Entities;

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
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .ThenInclude(m => m.ToolCalls)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) return false;
        var c = await db.Conversations
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        db.ChatMessages.RemoveRange(c.Messages);
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
}
