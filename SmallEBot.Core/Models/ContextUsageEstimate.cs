namespace SmallEBot.Core.Models;

/// <summary>Estimated context usage: ratio (0–1), used tokens, and context window size.</summary>
public record ContextUsageEstimate(double Ratio, int UsedTokens, int ContextWindowTokens);
