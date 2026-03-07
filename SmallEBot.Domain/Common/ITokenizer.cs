// SmallEBot.Domain/Common/Services/ITokenizer.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Tokenizer for counting tokens in text.
/// Pure domain interface with no external dependencies.
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// Counts the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to count tokens for.</param>
    /// <returns>The estimated number of tokens.</returns>
    int CountTokens(string text);
}
