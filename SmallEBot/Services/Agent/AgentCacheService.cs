using System.Text.Json.Serialization;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent;

/// <summary>Host service for agent cache invalidation and context usage estimation (UI).</summary>
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer) : IAsyncDisposable
{
    private const string FallbackSystemPromptForTokenCount = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";

    public async Task InvalidateAgentAsync() => await agentBuilder.InvalidateAsync();

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

    private static string SerializeRequestJsonForTokenCount(string systemPrompt, List<Core.Entities.ChatMessage> messages)
    {
        var payload = new RequestPayloadForTokenCount
        {
            System = systemPrompt,
            Messages = messages.Select(m => new MessageItemForTokenCount { Role = m.Role, Content = m.Content }).ToList()
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
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
