namespace SmallEBot.Application.Conversation;

using Core.Models;

/// <summary>Provides context usage estimation for compression threshold checking.</summary>
public interface IContextUsageEstimator
{
    /// <summary>Get detailed context usage estimate including ratio, used tokens, and context window size.</summary>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default);
}
