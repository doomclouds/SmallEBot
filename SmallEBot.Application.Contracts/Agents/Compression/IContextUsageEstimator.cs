using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Compression;

/// <summary>Provides context usage estimation for compression threshold checking and UI display.</summary>
public interface IContextUsageEstimator
{
    /// <summary>Get detailed context usage estimate including ratio, used tokens, and context window size.</summary>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Format token count for display, e.g. 128000 -> "128k", 10500 -> "10.5k".</summary>
    string FormatTokenCount(int tokens);
}
