// SmallEBot.Domain/Conversations/Services/ICompressionService.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Conversations.Services;

/// <summary>
/// Service for compressing conversation context.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// Generates a summary from messages, optionally merging with existing summary.
    /// </summary>
    /// <param name="messages">Messages to summarize.</param>
    /// <param name="toolResultMaxLength">Max length for tool results in the summary.</param>
    /// <param name="existingSummary">Existing compressed context to merge with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default);
}
