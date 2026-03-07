// SmallEBot.Domain/Conversations/Services/IContextWindowEstimator.cs
namespace SmallEBot.Domain.Conversations.Services;

/// <summary>
/// Estimates context window usage for a conversation.
/// </summary>
public interface IContextWindowEstimator
{
    /// <summary>
    /// Gets the estimated context usage for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Estimate with ratio, used tokens, and context window size.</returns>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct = default);
}

/// <summary>
/// Context usage estimate result.
/// </summary>
/// <param name="Ratio">Usage ratio (0.0-1.0).</param>
/// <param name="UsedTokens">Number of tokens used.</param>
/// <param name="ContextWindowTokens">Total context window size.</param>
public record ContextUsageEstimate(
    double Ratio,
    int UsedTokens,
    int ContextWindowTokens);
