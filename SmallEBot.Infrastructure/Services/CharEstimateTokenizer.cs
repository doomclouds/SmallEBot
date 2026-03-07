using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Extended tokenizer interface with encoding/decoding capabilities.
/// Inherit from domain ITokenizer for backward compatibility.
/// </summary>
public interface IFullTokenizer : ITokenizer
{
    List<int> Encode(string text);
    string Decode(List<int> tokens);
}

/// <summary>
/// Fallback token estimator when tokenizer.json is not available. Uses ~4 chars per token.
/// </summary>
public class CharEstimateTokenizer : IFullTokenizer
{
    public List<int> Encode(string text) => throw new NotSupportedException("CharEstimateTokenizer does not support Encode.");
    public string Decode(List<int> tokens) => throw new NotSupportedException("CharEstimateTokenizer does not support Decode.");
    public int CountTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);
}
