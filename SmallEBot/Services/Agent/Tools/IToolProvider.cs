using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides a set of AI tools.</summary>
public interface IToolProvider
{
    /// <summary>Provider name for identification.</summary>
    string Name { get; }

    /// <summary>Whether this provider is currently enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Get all tools from this provider.</summary>
    IEnumerable<AITool> GetTools();
}
