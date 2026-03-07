using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations.Compression;

/// <summary>Service for compressing conversation history using LLM.</summary>
public interface ICompressionService
{
    /// <summary>Generate a compressed summary of conversation history.</summary>
    /// <param name="messages">Chat messages to compress (tool calls are embedded in message contents).</param>
    /// <param name="toolResultMaxLength">Maximum length for truncated tool results.</param>
    /// <param name="existingSummary">Existing compressed summary to merge with new content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Compressed summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default);
}
