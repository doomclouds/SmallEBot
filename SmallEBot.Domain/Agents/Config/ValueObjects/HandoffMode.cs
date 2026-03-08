namespace SmallEBot.Domain.Agents.Config.ValueObjects;

/// <summary>
/// Mode of handoff between main agent and sub-agent.
/// </summary>
public enum HandoffMode
{
    /// <summary>
    /// Delegate: Execute task in sub-agent, return result to parent agent.
    /// </summary>
    Delegate = 0,

    /// <summary>
    /// Handoff: Transfer conversation control to sub-agent.
    /// </summary>
    Handoff = 1
}
