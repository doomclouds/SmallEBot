using SmallEBot.Application.Context;
using SmallEBot.Core.Entities;
using SmallEBot.Services.Agent;

namespace SmallEBot.Services.Context;

/// <summary>Manages context window using tokenizer for estimation.</summary>
public sealed class ContextWindowManager(ITokenizer tokenizer) : IContextWindowManager
{
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
