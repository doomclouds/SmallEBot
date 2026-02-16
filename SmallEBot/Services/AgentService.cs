using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services;

public class AgentService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer) : IAsyncDisposable
{
    /// <summary>Fallback system prompt when builder has not built yet (for token estimation).</summary>
    private const string FallbackSystemPromptForTokenCount = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";

    /// <summary>Invalidates cached agent and MCP clients so the next request rebuilds with current MCP config (e.g. after enable/disable toggle).</summary>
    public async Task InvalidateAgentAsync() => await agentBuilder.InvalidateAsync();

    /// <summary>Estimated context usage (0.0–1.0) from tokenized request body (system + messages as JSON). Inflated by 5%; result rounded to 0.1%.</summary>
    public async Task<double> GetEstimatedContextUsageAsync(Guid conversationId, CancellationToken ct = default)
    {
        var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
        var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;
        var json = SerializeRequestJsonForTokenCount(systemPrompt, messages);
        var rawTokens = tokenizer.CountTokens(json);
        var withBuffer = (int)Math.Ceiling(rawTokens * 1.05);
        var contextWindow = agentBuilder.GetContextWindowTokens();
        var ratio = contextWindow <= 0 ? 0.0 : Math.Min(1.0, withBuffer / (double)contextWindow);
        return Math.Round(ratio, 3);
    }

    public async IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, ct);

        var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
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
        var msgCountBefore = await conversationRepository.GetMessageCountAsync(conversationId, ct);
        var newTitle = msgCountBefore == 0 ? await GenerateTitleAsync(userMessage, ct) : null;
        return await conversationRepository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, ct);
    }

    /// <summary>Persist assistant segments for an existing turn.</summary>
    public async Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> assistantSegments,
        CancellationToken ct = default) =>
        await conversationRepository.CompleteTurnWithAssistantAsync(conversationId, turnId, assistantSegments, ct);

    /// <summary>Persist an error message as the assistant reply for the turn.</summary>
    public async Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken ct = default) =>
        await conversationRepository.CompleteTurnWithErrorAsync(conversationId, turnId, errorMessage, ct);

    /// <summary>Serializes system + messages as request JSON (same shape as HTTP body) for accurate token count.</summary>
    private static string SerializeRequestJsonForTokenCount(string systemPrompt, List<Core.Entities.ChatMessage> messages)
    {
        var payload = new RequestPayloadForTokenCount
        {
            System = systemPrompt,
            Messages = messages.Select(m => new MessageItemForTokenCount { Role = m.Role, Content = m.Content }).ToList()
        };
        return JsonSerializer.Serialize(payload);
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
}
