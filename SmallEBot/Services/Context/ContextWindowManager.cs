using SmallEBot.Application.Context;
using SmallEBot.Core.Entities;
using SmallEBot.Services.Agent;

namespace SmallEBot.Services.Context;

/// <summary>Manages context window using tokenizer for estimation. Note: EstimateTokens and TrimToFit count only message Content (and a small role overhead); they do not include tool calls (name, arguments, result) or think blocks. The UI context-usage estimate in AgentCacheService includes those via a separate payload.</summary>
public sealed class ContextWindowManager(ITokenizer tokenizer) : IContextWindowManager
{
    /// <summary>Estimates tokens for the given messages. Counts only each message's Content plus role overhead; tool calls and think blocks are not included.</summary>
    public int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return 0;
        var total = 0;
        foreach (var msg in messages)
        {
            total += tokenizer.CountTokens(msg.Content);
            total += 4; // role overhead estimate
        }
        return total;
    }

    /// <summary>Trims messages to fit within maxTokens. Only message Content is considered; tool/think tokens are not part of this budget.</summary>
    public TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        if (messages.Count == 0)
            return new TrimResult([], 0, 0);

        var tokens = EstimateTokens(messages);
        if (tokens <= maxTokens)
            return new TrimResult(messages, tokens, 0);

        // Keep newest messages, trim oldest
        var result = new List<ChatMessage>();
        var currentTokens = 0;
        var trimmed = 0;

        // Iterate from newest to oldest
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var msgTokens = tokenizer.CountTokens(msg.Content) + 4;
            if (currentTokens + msgTokens <= maxTokens)
            {
                result.Insert(0, msg);
                currentTokens += msgTokens;
            }
            else
            {
                trimmed++;
            }
        }

        return new TrimResult(result, currentTokens, trimmed);
    }
}
