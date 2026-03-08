using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Compression;

/// <summary>Provides context usage estimation for compression threshold checking.</summary>
public interface IContextUsageEstimator
{
    /// <summary>Get detailed context usage estimate including ratio, used tokens, and context window size.</summary>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default);
}
