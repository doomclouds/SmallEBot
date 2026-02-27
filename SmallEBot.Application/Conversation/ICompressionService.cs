using SmallEBot.Core.Entities;

namespace SmallEBot.Application.Conversation;

/// <summary>Service for compressing conversation history using LLM.</summary>
public interface ICompressionService
{
    /// <summary>Generate a compressed summary of conversation history.</summary>
    /// <param name="messages">Chat messages to compress.</param>
    /// <param name="toolCalls">Tool calls to include in compression.</param>
    /// <param name="toolResultMaxLength">Maximum length for truncated tool results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Compressed summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default);
}
