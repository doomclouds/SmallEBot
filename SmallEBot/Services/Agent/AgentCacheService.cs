using System.Text.Json.Serialization;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent;

/// <summary>Estimated context usage: ratio (0–1), used tokens, and context window size.</summary>
public record ContextUsageEstimate(double Ratio, int UsedTokens, int ContextWindowTokens);

/// <summary>Host service for agent cache invalidation and context usage estimation (UI).</summary>
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer) : IAsyncDisposable
{
    private const string FallbackSystemPromptForTokenCount = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";

    public async Task InvalidateAgentAsync() => await agentBuilder.InvalidateAsync();

    /// <summary>Estimated context usage for UI: ratio and token counts (e.g. for tooltip "8% · 10k/128k"). Includes system, messages, tool calls (name + arguments + result), and think blocks.</summary>
    public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default)
    {
        var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
        // var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);
        // var thinkBlocks = await conversationRepository.GetThinkBlocksForConversationAsync(conversationId, ct);
        var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;
        var json = SerializeRequestJsonForTokenCount(systemPrompt, messages, [], []);
        var rawTokens = tokenizer.CountTokens(json);
        var usedTokens = (int)Math.Ceiling(rawTokens * 1.05);
        var contextWindow = agentBuilder.GetContextWindowTokens();
        if (contextWindow <= 0) return new ContextUsageEstimate(0, usedTokens, contextWindow);
        var ratio = Math.Min(1.0, usedTokens / (double)contextWindow);
        return new ContextUsageEstimate(Math.Round(ratio, 3), usedTokens, contextWindow);
    }

    public async Task<double> GetEstimatedContextUsageAsync(Guid conversationId, CancellationToken ct = default)
    {
        var d = await GetEstimatedContextUsageDetailAsync(conversationId, ct);
        return d?.Ratio ?? 0.0;
    }

    /// <summary>Format token count for tooltip, e.g. 128000 -> "128k", 10500 -> "10.5k".</summary>
    public static string FormatTokenCount(int tokens)
    {
        if (tokens < 1000) return tokens.ToString();
        var k = tokens / 1000.0;
        return $"{k:F1}k";
    }

    private static string SerializeRequestJsonForTokenCount(
        string systemPrompt,
        List<Core.Entities.ChatMessage> messages,
        List<Core.Entities.ToolCall> toolCalls,
        List<Core.Entities.ThinkBlock> thinkBlocks)
    {
        var payload = new RequestPayloadForTokenCount
        {
            System = systemPrompt,
            Messages = messages.Select(m => new MessageItemForTokenCount { Role = m.Role, Content = m.Content ?? "" }).ToList(),
            ToolCalls = toolCalls.Select(t => new ToolCallItemForTokenCount
            {
                ToolName = t.ToolName ?? "",
                Arguments = t.Arguments ?? "",
                Result = t.Result ?? ""
            }).ToList(),
            ThinkBlocks = thinkBlocks.Select(b => new ThinkBlockItemForTokenCount { Content = b.Content ?? "" }).ToList()
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

        [JsonPropertyName("toolCalls")]
        public List<ToolCallItemForTokenCount> ToolCalls { get; set; } = [];

        [JsonPropertyName("thinkBlocks")]
        public List<ThinkBlockItemForTokenCount> ThinkBlocks { get; set; } = [];
    }

    private sealed class MessageItemForTokenCount
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ToolCallItemForTokenCount
    {
        [JsonPropertyName("toolName")]
        public string ToolName { get; set; } = "";

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = "";

        [JsonPropertyName("result")]
        public string Result { get; set; } = "";
    }

    private sealed class ThinkBlockItemForTokenCount
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
