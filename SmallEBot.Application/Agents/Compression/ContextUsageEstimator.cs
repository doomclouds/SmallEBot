using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Agents.Config;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Agents.Compression;

/// <summary>Estimates context token usage for compression threshold and UI display.</summary>
public sealed class ContextUsageEstimator(
    IConversationMessageStore messageStore,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer,
    IAgentConfigService agentConfig,
    IConversationMetadataRepository metadataRepository) : IContextUsageEstimator
{
    private const string FallbackSystemPromptForTokenCount = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";

    /// <summary>Estimated context usage for UI: ratio and token counts (e.g. for tooltip "8% · 10k/128k"). Includes system, messages, tool calls (name + arguments + result), think blocks, and compressed context.</summary>
    public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, ct);
        var allMessages = await messageStore.GetMessagesAsync(conversationId, ct);
        var toolResultMaxLength = await agentConfig.GetToolResultMaxLengthAsync(ct);

        var filteredMessages = FilterMessagesByCompressedAt(allMessages, metadata);
        var truncatedToolCalls = ExtractToolCalls(filteredMessages, toolResultMaxLength);

        var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;

        var compressedContextTokens = 0;
        if (!string.IsNullOrEmpty(metadata?.CompressedContext))
        {
            compressedContextTokens = tokenizer.CountTokens(metadata.CompressedContext);
        }

        var json = SerializeRequestJsonForTokenCount(systemPrompt, filteredMessages, truncatedToolCalls, []);
        var rawTokens = tokenizer.CountTokens(json);
        var usedTokens = (int)Math.Ceiling(rawTokens * 1.05) + compressedContextTokens;
        var contextWindow = await agentBuilder.GetContextWindowTokensAsync(ct);
        if (contextWindow <= 0) return new ContextUsageEstimate(0, usedTokens, contextWindow);
        var ratio = Math.Min(1.0, usedTokens / (double)contextWindow);
        return new ContextUsageEstimate(Math.Round(ratio, 3), usedTokens, contextWindow);
    }

    public string FormatTokenCount(int tokens)
    {
        if (tokens < 1000) return tokens.ToString();
        var k = tokens / 1000.0;
        return $"{k:F1}k";
    }

    private static IReadOnlyList<ChatMessage> FilterMessagesByCompressedAt(
        IReadOnlyList<ChatMessage> allMessages,
        ConversationMetadata? metadata)
    {
        if (metadata?.CompressedAt == null || metadata.Turns.Count == 0)
            return allMessages;

        var keepIndices = new HashSet<int>();
        var turns = metadata.Turns;
        for (var k = 0; k < turns.Count; k++)
        {
            if (turns[k].CreatedAt <= metadata.CompressedAt.Value)
                continue;
            var start = turns[k].FirstMessageIndex;
            var end = k + 1 < turns.Count ? turns[k + 1].FirstMessageIndex : allMessages.Count;
            for (var i = start; i < end && i < allMessages.Count; i++)
                keepIndices.Add(i);
        }

        return allMessages
            .Select((m, i) => (Message: m, Index: i))
            .Where(x => keepIndices.Contains(x.Index))
            .Select(x => x.Message)
            .ToList();
    }

    private static List<ToolCallWithTruncatedResult> ExtractToolCalls(IReadOnlyList<ChatMessage> messages, int toolResultMaxLength)
    {
        var result = new List<ToolCallWithTruncatedResult>();

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fnCall)
                {
                    result.Add(new ToolCallWithTruncatedResult
                    {
                        ToolName = fnCall.Name ?? "",
                        Arguments = ToJsonString(fnCall.Arguments) ?? "",
                        Result = ""
                    });
                }
                else if (content is FunctionResultContent fnResult)
                {
                    var matchingCall = result.FirstOrDefault(t => t.ToolName == fnResult.CallId);
                    if (matchingCall != null)
                    {
                        matchingCall.Result = TruncateToolResult(fnResult.Result?.ToString(), toolResultMaxLength) ?? "";
                    }
                }
            }
        }

        return result;
    }

    private static string? TruncateToolResult(string? result, int maxLength)
    {
        if (result == null) return null;
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    private static string? ToJsonString(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
            return "{}";
        return System.Text.Json.JsonSerializer.Serialize(arguments);
    }

    private static string SerializeRequestJsonForTokenCount(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        List<ToolCallWithTruncatedResult> toolCalls,
        List<ThinkBlockInfo> thinkBlocks)
    {
        var payload = new RequestPayloadForTokenCount
        {
            System = systemPrompt,
            Messages = messages.Select(m => new MessageItemForTokenCount { Role = m.Role.ToString(), Content = m.Text ?? "" }).ToList(),
            ToolCalls = toolCalls.Select(t => new ToolCallItemForTokenCount
            {
                ToolName = t.ToolName,
                Arguments = t.Arguments,
                Result = t.Result
            }).ToList(),
            ThinkBlocks = thinkBlocks.Select(b => new ThinkBlockItemForTokenCount { Content = b.Content }).ToList()
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
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

    private sealed class ToolCallWithTruncatedResult
    {
        public string ToolName { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string Result { get; set; } = "";
    }
}
