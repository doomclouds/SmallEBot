using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Conversations;

namespace SmallEBot.Services.Agent;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService(IAgentBuilder agentBuilder, ILogger<CompressionService> logger) : ICompressionService
{
    private const string CompactPrompt = """
                                         You are compressing conversation history to save context space.

                                         ## Input
                                         You will receive:
                                         1. Previous summary (if exists) - already compressed content
                                         2. New conversation messages to compress

                                         ## Task
                                         Generate a MERGED summary that combines the previous summary with the new messages.
                                         Preserve all important information, update state as needed.

                                         ## Format
                                         Use this compact format:

                                         ## Summary
                                         [1-2 sentences overview]

                                         ## Decisions
                                         - [decision]: [reasoning]

                                         ## Files
                                         - path/to/file: [change summary]

                                         ## State
                                         - Done: [items]
                                         - Pending: [items]

                                         ## Context
                                         - [key=value pairs or important notes]

                                         Keep total output under 800 tokens. Focus on what's needed to continue the work.
                                         """;

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default)
    {
        if (messages.Count == 0 && string.IsNullOrEmpty(existingSummary))
            return existingSummary;

        var sb = new StringBuilder();

        // Include existing summary if present
        if (!string.IsNullOrEmpty(existingSummary))
        {
            sb.AppendLine("## Previous Summary (merge with new messages)");
            sb.AppendLine(existingSummary);
            sb.AppendLine();
        }

        if (messages.Count > 0)
        {
            sb.AppendLine("## New Messages to Compress");
            sb.AppendLine();

            // Process messages and extract content including tool calls embedded in Contents
            foreach (var message in messages)
            {
                var role = message.Role == ChatRole.User ? "User" : "Assistant";
                sb.AppendLine($"[{role}]:");

                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        sb.AppendLine(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoning)
                    {
                        var reasoningPreview = reasoning.Text.Length > 200
                            ? reasoning.Text[..200] + "..."
                            : reasoning.Text;
                        sb.AppendLine($"[Thinking]: {reasoningPreview}");
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        sb.AppendLine($"[Tool: {fnCall.Name}]");
                        sb.AppendLine($"Arguments: {ToJsonString(fnCall.Arguments)}");
                    }
                    else if (content is FunctionResultContent fnResult)
                    {
                        var result = TruncateResult(fnResult.Result?.ToString(), toolResultMaxLength);
                        sb.AppendLine($"[Tool Result]: {result}");
                    }
                }

                sb.AppendLine();
            }
        }

        try
        {
            var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, ct);
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, CompactPrompt),
                new(ChatRole.User, sb.ToString())
            };

            var chatOptions = new ChatOptions { Reasoning = null };
            var runOptions = new ChatClientAgentRunOptions(chatOptions);
            var result = await agent.RunAsync(chatMessages, null, runOptions, ct);
            logger.LogInformation("Compression generated summary: {Length} chars", result.Text.Length);
            return result.Text;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate compression summary");
            return null;
        }
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    private static string ToJsonString(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(arguments);
    }
}
