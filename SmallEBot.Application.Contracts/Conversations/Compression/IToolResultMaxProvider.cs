namespace SmallEBot.Application.Contracts.Conversations.Compression;

/// <summary>Provides the maximum length for tool call results.</summary>
public interface IToolResultMaxProvider
{
    /// <summary>Gets the maximum length for tool call results in characters.</summary>
    int GetToolResultMaxLength();
}
