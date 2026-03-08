using Tokenizers.DotNet;

namespace SmallEBot.Infrastructure.Agents.Tokenizers;

/// <summary>
/// DeepSeek v3 Tokenizer implementation, using Tokenizers.DotNet library to load tokenizer.json
/// Corresponds to Python version deepseek_v3_tokenizer
/// Defaults to using tokenizer.json file in the running directory
/// </summary>
public class DeepSeekTokenizer : IFullTokenizer, IDisposable
{
    private readonly Tokenizer _tokenizer;

    /// <summary>
    /// Initialize DeepSeekTokenizer
    /// </summary>
    /// <param name="tokenizerJsonPath">
    /// Path to tokenizer.json file (optional)
    /// - If empty or null, defaults to tokenizer.json in the running directory
    /// - If path is provided, can be absolute path or relative to running directory
    /// </param>
    /// <remarks>
    /// By default, looks for tokenizer.json file in the running directory (Directory.GetCurrentDirectory())
    /// </remarks>
    public DeepSeekTokenizer(string? tokenizerJsonPath = null)
    {
        string tokenizerPath;
        // Determine full path to tokenizer.json
        if (string.IsNullOrEmpty(tokenizerJsonPath))
        {
            // Default to tokenizer.json in running directory
            tokenizerPath = Path.Combine(Directory.GetCurrentDirectory(), "tokenizer.json");
        }
        else if (Path.IsPathRooted(tokenizerJsonPath))
        {
            // If absolute path, use directly
            tokenizerPath = tokenizerJsonPath;
        }
        else
        {
            // If relative path, relative to running directory
            tokenizerPath = Path.Combine(Directory.GetCurrentDirectory(), tokenizerJsonPath);
        }

        // Check if file exists
        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException(
                $"Tokenizer file not found: {tokenizerPath}. Please ensure tokenizer.json file exists at the specified location.");
        }

        // Load tokenizer using Tokenizers.DotNet
        _tokenizer = new Tokenizer(vocabPath: tokenizerPath);
    }

    /// <summary>
    /// Encode text to token ID list
    /// </summary>
    public List<int> Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = _tokenizer.Encode(text);
        return tokens.Select(t => (int)t).ToList();
    }

    /// <summary>
    /// Decode token ID list to text
    /// </summary>
    public string Decode(List<int> tokens)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var uintTokens = tokens.Select(t => (uint)t).ToArray();
        return _tokenizer.Decode(uintTokens);
    }

    /// <summary>
    /// Count tokens in text
    /// </summary>
    public int CountTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : Encode(text).Count;
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        _tokenizer.Dispose();
    }
}
