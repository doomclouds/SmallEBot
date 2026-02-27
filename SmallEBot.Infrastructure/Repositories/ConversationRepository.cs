using System.Text.Json;
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
            .AsNoTracking()
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

    public async Task<List<Conversation>> SearchAsync(
        string userName,
        string query,
        bool includeContent = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetListAsync(userName, ct);

        return await db.Conversations
            .Where(c => c.UserName == userName && EF.Functions.Like(c.Title, $"%{query}%"))
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<ChatMessage>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ChatMessages
            .Where(x => x.ConversationId == conversationId && x.ReplacedByMessageId == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<ToolCall>> GetToolCallsForConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ToolCalls
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<ThinkBlock>> GetThinkBlocksForConversationAsync(Guid conversationId, CancellationToken ct = default) =>
        await db.ThinkBlocks
            .AsNoTracking()
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

    public async Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, IReadOnlyList<string>? attachedPaths = null, IReadOnlyList<string>? requestedSkillIds = null, CancellationToken ct = default)
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
            CreatedAt = baseTime,
            AttachedPathsJson = (attachedPaths?.Count ?? 0) > 0 ? JsonSerializer.Serialize(attachedPaths) : null,
            RequestedSkillIdsJson = (requestedSkillIds?.Count ?? 0) > 0 ? JsonSerializer.Serialize(requestedSkillIds) : null
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
        var conv = await db.Conversations
            .Include(c => c.Turns.OrderBy(t => t.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) return null;

        var oldMsg = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId && m.Role == "user" && m.ReplacedByMessageId == null, ct);
        if (oldMsg == null) return null;

        var turns = conv.Turns.OrderBy(t => t.CreatedAt).ToList();
        var turnIndex = turns.FindIndex(t => t.Id == oldMsg.TurnId);
        if (turnIndex < 0) return null;

        var effectivePaths = attachedPaths ?? oldMsg.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? oldMsg.RequestedSkillIds;

        var baseTime = DateTime.UtcNow;
        var newTurn = new ConversationTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            IsThinkingMode = useThinking,
            CreatedAt = baseTime
        };
        db.ConversationTurns.Add(newTurn);

        var newMsg = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnId = newTurn.Id,
            Role = "user",
            Content = newContent.Trim(),
            CreatedAt = baseTime.AddMilliseconds(1),
            IsEdited = true,
            AttachedPathsJson = effectivePaths.Count > 0 ? JsonSerializer.Serialize(effectivePaths) : null,
            RequestedSkillIdsJson = effectiveSkills.Count > 0 ? JsonSerializer.Serialize(effectiveSkills) : null
        };
        db.ChatMessages.Add(newMsg);
        oldMsg.ReplacedByMessageId = newMsg.Id;

        var turnToClear = oldMsg.TurnId;
        var assistantMessages = await db.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.TurnId == turnToClear && m.Role == "assistant")
            .ToListAsync(ct);
        db.ChatMessages.RemoveRange(assistantMessages);

        var toolCalls = await db.ToolCalls.Where(t => t.ConversationId == conversationId && t.TurnId == turnToClear).ToListAsync(ct);
        db.ToolCalls.RemoveRange(toolCalls);

        var thinkBlocks = await db.ThinkBlocks.Where(b => b.ConversationId == conversationId && b.TurnId == turnToClear).ToListAsync(ct);
        db.ThinkBlocks.RemoveRange(thinkBlocks);

        for (var i = turnIndex + 1; i < turns.Count; i++)
        {
            var t = turns[i];
            var msgs = await db.ChatMessages.Where(m => m.ConversationId == conversationId && m.TurnId == t.Id).ToListAsync(ct);
            db.ChatMessages.RemoveRange(msgs);
            var tc = await db.ToolCalls.Where(x => x.ConversationId == conversationId && x.TurnId == t.Id).ToListAsync(ct);
            db.ToolCalls.RemoveRange(tc);
            var tb = await db.ThinkBlocks.Where(x => x.ConversationId == conversationId && x.TurnId == t.Id).ToListAsync(ct);
            db.ThinkBlocks.RemoveRange(tb);
            db.ConversationTurns.Remove(t);
        }

        conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (newTurn.Id, newMsg.Content, effectivePaths, effectiveSkills);
    }

    public async Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> GetTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .Include(c => c.Turns.OrderBy(t => t.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) return null;

        var turn = conv.Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn == null) return null;

        var userMsg = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.TurnId == turnId && m.Role == "user" && m.ReplacedByMessageId == null, ct);
        if (userMsg == null) return null;

        var turns = conv.Turns.OrderBy(t => t.CreatedAt).ToList();
        var turnIndex = turns.IndexOf(turn);
        if (turnIndex < 0) return null;

        var assistantMessages = await db.ChatMessages
            .Where(m => m.ConversationId == conversationId && m.TurnId == turnId && m.Role == "assistant")
            .ToListAsync(ct);
        db.ChatMessages.RemoveRange(assistantMessages);

        var toolCalls = await db.ToolCalls.Where(t => t.ConversationId == conversationId && t.TurnId == turnId).ToListAsync(ct);
        db.ToolCalls.RemoveRange(toolCalls);

        var thinkBlocks = await db.ThinkBlocks.Where(b => b.ConversationId == conversationId && b.TurnId == turnId).ToListAsync(ct);
        db.ThinkBlocks.RemoveRange(thinkBlocks);

        for (var i = turnIndex + 1; i < turns.Count; i++)
        {
            var t = turns[i];
            var msgs = await db.ChatMessages.Where(m => m.ConversationId == conversationId && m.TurnId == t.Id).ToListAsync(ct);
            db.ChatMessages.RemoveRange(msgs);
            var tc = await db.ToolCalls.Where(x => x.ConversationId == conversationId && x.TurnId == t.Id).ToListAsync(ct);
            db.ToolCalls.RemoveRange(tc);
            var tb = await db.ThinkBlocks.Where(x => x.ConversationId == conversationId && x.TurnId == t.Id).ToListAsync(ct);
            db.ThinkBlocks.RemoveRange(tb);
            db.ConversationTurns.Remove(t);
        }

        conv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (turn.Id, userMsg.Content, turn.IsThinkingMode, userMsg.AttachedPaths, userMsg.RequestedSkillIds);
    }

    public async Task UpdateCompressionAsync(Guid conversationId, string? compressedContext, DateTime? compressedAt, CancellationToken ct = default)
    {
        var conv = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conv == null) return;

        conv.CompressedContext = compressedContext;
        conv.CompressedAt = compressedAt;
        conv.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
