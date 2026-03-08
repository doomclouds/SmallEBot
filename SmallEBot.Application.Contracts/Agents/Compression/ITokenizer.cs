namespace SmallEBot.Application.Contracts.Agents.Compression;

/// <summary>
/// Tokenizer for counting tokens in text. Used for context usage estimation and compression.
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
