namespace SmallEBot.Application.Contracts.Agents.Execution;

/// <summary>Invalidates the cached agent when config changes (MCP, skills, model). Call after user saves config.</summary>
public interface IAgentInvalidationService
{
    Task InvalidateAgentAsync();
}
