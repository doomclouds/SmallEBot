using SmallEBot.Core.Entities;

namespace SmallEBot.Application.Context;

/// <summary>Manages context window for conversations.</summary>
public interface IContextWindowManager
{
    /// <summary>Trim messages to fit within token limit.</summary>
    TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens);
}

/// <summary>Result of trimming messages.</summary>
public record TrimResult(
    IReadOnlyList<ChatMessage> Messages,
    int TotalTokens,
    int TrimmedCount);
