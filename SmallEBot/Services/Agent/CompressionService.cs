using System.Text;
using Microsoft.Extensions.AI;
using SmallEBot.Core.Entities;

namespace SmallEBot.Services.Agent;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService : ICompressionService
{
    private readonly IChatClient _chatClient;

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

    public CompressionService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## Conversation to Compress");
        sb.AppendLine();

        foreach (var msg in messages.Where(m => m.ReplacedByMessageId == null))
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"[{role}]: {msg.Content}");
            sb.AppendLine();
        }

        foreach (var tc in toolCalls)
        {
            var result = TruncateResult(tc.Result, toolResultMaxLength);
            sb.AppendLine($"[Tool: {tc.ToolName}] -> {result}");
            sb.AppendLine();
        }

        var messagesForLlm = new List<ChatMessage>
        {
            new(ChatRole.System, CompactPrompt),
            new(ChatRole.User, sb.ToString())
        };

        try
        {
            var response = await _chatClient.CompleteAsync(messagesForLlm, cancellationToken: ct);
            return response.Message.Text;
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
