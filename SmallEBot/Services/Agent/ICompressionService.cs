using SmallEBot.Core.Entities;

namespace SmallEBot.Services.Agent;

/// <summary>Service for compressing conversation history using LLM.</summary>
public interface ICompressionService
{
    /// <summary>Generate a compressed summary of conversation history.</summary>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default);
}
