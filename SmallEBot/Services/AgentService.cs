using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SmallEBot.Data;
using SmallEBot.Data.Entities;
using SmallEBot.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services;

public class AgentService(
    AppDbContext db,
    ConversationService convSvc,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer) : IAsyncDisposable
{
    /// <summary>Fallback system prompt when builder has not built yet (for token estimation).</summary>
    private const string FallbackSystemPromptForTokenCount = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";

    /// <summary>Invalidates cached agent and MCP clients so the next request rebuilds with current MCP config (e.g. after enable/disable toggle).</summary>
    public async Task InvalidateAgentAsync() => await agentBuilder.InvalidateAsync();

    /// <summary>Context window size in tokens (e.g. 128000 for DeepSeek). Used for context % display.</summary>
    public int GetContextWindowTokens() => agentBuilder.GetContextWindowTokens();

    /// <summary>Estimated context usage (0.0–1.0) from tokenized request body (system + messages as JSON). Inflated by 5%; result rounded to 0.1%.</summary>
    public async Task<double> GetEstimatedContextUsageAsync(Guid conversationId, CancellationToken ct = default)
    {
        var store = new ChatMessageStoreAdapter(db, conversationId);
        var messages = await store.LoadMessagesAsync(ct);
        var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;
        var json = SerializeRequestJsonForTokenCount(systemPrompt, messages);
        var rawTokens = tokenizer.CountTokens(json);
        var withBuffer = (int)Math.Ceiling(rawTokens * 1.05);
        var contextWindow = agentBuilder.GetContextWindowTokens();
        var ratio = contextWindow <= 0 ? 0.0 : Math.Min(1.0, withBuffer / (double)contextWindow);
        return Math.Round(ratio, 3);
    }

    /// <summary>Serializes system + messages as request JSON (same shape as HTTP body) for accurate token count.</summary>
    private static string SerializeRequestJsonForTokenCount(string systemPrompt, List<Data.Entities.ChatMessage> messages)
    {
        var payload = new RequestPayloadForTokenCount
        {
            System = systemPrompt,
            Messages = messages.Select(m => new MessageItemForTokenCount { Role = m.Role, Content = m.Content }).ToList()
        };
        return JsonSerializer.Serialize(payload);
    }

    private sealed class RequestPayloadForTokenCount
    {
        [JsonPropertyName("system")]
        public string System { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<MessageItemForTokenCount> Messages { get; set; } = [];
    }

    private sealed class MessageItemForTokenCount
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    public async IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, ct);

        var store = new ChatMessageStoreAdapter(db, conversationId);
        var history = await store.LoadMessagesAsync(ct);
        var frameworkMessages = history
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, ct))
        {
            if (update.Contents is { Count: > 0 } contents)
            {
                foreach (var content in contents)
                {
                    switch (content)
                    {
                        case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                            yield return new TextStreamUpdate(textContent.Text);
                            break;
                        case TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text):
                            yield return new ThinkStreamUpdate(reasoningContent.Text);
                            break;
                        case FunctionCallContent fnCall:
                            yield return new ToolCallStreamUpdate(fnCall.Name, ToJsonString(fnCall.Arguments));
                            break;
                        case FunctionResultContent fnResult:
                            yield return new ToolCallStreamUpdate(fnResult.CallId, Result: ToJsonString(fnResult.Result));
                            break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextStreamUpdate(update.Text);
            }
        }
    }

    /// <summary>Creates a turn and persists the user message. Call before streaming. Returns turn Id.</summary>
    public async Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null)
            throw new InvalidOperationException("Conversation not found.");

        var msgCountBefore = await convSvc.GetMessageCountAsync(conversationId, ct);
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

        db.ChatMessages.Add(new Data.Entities.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnId = turn.Id,
            Role = "user",
            Content = userMessage,
            CreatedAt = baseTime
        });

        conv.UpdatedAt = DateTime.UtcNow;
        if (msgCountBefore == 0)
        {
            var title = await GenerateTitleAsync(userMessage, ct);
            conv.Title = title;
        }

        await db.SaveChangesAsync(ct);
        return turn.Id;
    }

    /// <summary>Persist assistant segments for an existing turn.</summary>
    public async Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> assistantSegments,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conv == null) return;

        var baseTime = DateTime.UtcNow;
        var toolOrder = 0;
        var thinkOrder = 0;

        foreach (var seg in assistantSegments)
        {
            if (seg.IsText && !string.IsNullOrEmpty(seg.Text))
            {
                db.ChatMessages.Add(new Data.Entities.ChatMessage
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

    /// <summary>Persist an error message as the assistant reply for the turn.</summary>
    public async Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conv == null) return;

        var content = "Error: " + (string.IsNullOrEmpty(errorMessage) ? "Unknown error" : errorMessage);
        db.ChatMessages.Add(new Data.Entities.ChatMessage
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

    private static string? ToJsonString(object? value)
    {
        if (value == null) return null;
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            try
            {
                using var doc = JsonDocument.Parse(s);
                return JsonSerializer.Serialize(doc.RootElement, jsonOptions);
            }
            catch { return s; }
        }
        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), jsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static ChatRole ToChatRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User
    };

    private async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken ct = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, ct);
        var prompt = $"Generate a very short title (under 20 chars, no quotes) for a conversation that starts with: {firstMessage}";
        try
        {
            var result = await agent.RunAsync(prompt, null, null, ct);
            var t = result.Text.Trim();
            if (t.Length > 20) t = t[..20];
            return string.IsNullOrEmpty(t) ? "新对话" : t;
        }
        catch
        {
            return firstMessage.Length > 20 ? firstMessage[..20] + "…" : firstMessage;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await agentBuilder.InvalidateAsync();
        GC.SuppressFinalize(this);
    }
}
