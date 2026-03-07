// SmallEBot.Domain/Agents/SubAgentConfig.cs
using SmallEBot.Domain.Agents.ValueObjects;
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Agents;

/// <summary>
/// Configuration for a sub-agent within an agent.
/// Sub-agents can be delegated specific tasks or handed off conversation control.
/// </summary>
public class SubAgentConfig : IEntity<string>
{
    public string Id { get; init; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Instructions for this sub-agent. Can override or append to parent agent's instructions.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// Optional model override. If null, uses parent agent's model.
    /// </summary>
    public ModelConfig? ModelOverride { get; set; }

    /// <summary>
    /// Tool set for this sub-agent. If null, inherits parent's tools based on InheritParent flag.
    /// </summary>
    public ToolSet? Tools { get; set; }

    /// <summary>
    /// Mode of interaction between parent and sub-agent.
    /// </summary>
    public HandoffMode HandoffMode { get; set; }

    /// <summary>
    /// Whether this sub-agent is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public SubAgentConfig(
        string id,
        string name,
        string description,
        string instructions,
        HandoffMode handoffMode = HandoffMode.Delegate)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
        HandoffMode = handoffMode;
    }
}
