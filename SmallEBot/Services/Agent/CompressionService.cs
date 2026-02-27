using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Entities;

// Aliases to avoid ambiguity between Microsoft.Extensions.AI and SmallEBot.Core.Entities
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using EntityChatMessage = SmallEBot.Core.Entities.ChatMessage;
using EntityToolCall = SmallEBot.Core.Entities.ToolCall;

namespace SmallEBot.Services.Agent;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService : ICompressionService
{
    private readonly IAgentBuilder _agentBuilder;

    private const string CompactPrompt = """
You are compressing conversation history to save context space.

## Input
You will receive conversation messages (user + assistant + tool calls).

## Task
Generate a structured summary preserving:

1. **Key Decisions**: Important choices made and why
2. **Files Modified**: Paths and what changed (briefly)
3. **Current State**: What's been accomplished, what's pending
4. **Important Context**: Names, values, configurations that matter

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

Keep total output under 500 tokens. Focus on what's needed to continue the work.
""";

    public CompressionService(IAgentBuilder agentBuilder)
    {
        _agentBuilder = agentBuilder;
    }

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<EntityChatMessage> messages,
        IReadOnlyList<EntityToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default)
    {
        if (messages.Count == 0 && toolCalls.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## Conversation to Compress");
        sb.AppendLine();

        // Add messages (exclude replaced ones)
        foreach (var msg in messages.Where(m => m.ReplacedByMessageId == null))
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"[{role}]: {msg.Content}");
            sb.AppendLine();
        }

        // Add tool calls with truncated results
        foreach (var tc in toolCalls)
        {
            var result = TruncateResult(tc.Result, toolResultMaxLength);
            sb.AppendLine($"[Tool: {tc.ToolName}] -> {result}");
            sb.AppendLine();
        }

        try
        {
            var agent = await _agentBuilder.GetOrCreateAgentAsync(useThinking: false, ct);
            var chatMessages = new List<AIChatMessage>
            {
                new(ChatRole.System, CompactPrompt),
                new(ChatRole.User, sb.ToString())
            };

            var chatOptions = new ChatOptions { Reasoning = null };
            var runOptions = new ChatClientAgentRunOptions(chatOptions);
            var result = await agent.RunAsync(chatMessages, null, runOptions, ct);
            return result.Text;
        }
        catch
        {
            return null;
        }
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }
}
