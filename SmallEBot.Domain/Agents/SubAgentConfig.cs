// SmallEBot.Domain/Agents/SubAgentConfig.cs
using SmallEBot.Domain.Agents.ValueObjects;
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Agents;

/// <summary>
/// Configuration for a sub-agent within an agent.
/// Sub-agents can be delegated specific tasks or handed off conversation control.
/// </summary>
public class SubAgentConfig(
    string id,
    string name,
    string description,
    string instructions,
    HandoffMode handoffMode = HandoffMode.Delegate)
    : IEntity<string>
{
    public string Id { get; init; } = id ?? throw new ArgumentNullException(nameof(id));
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string Description { get; set; } = description ?? throw new ArgumentNullException(nameof(description));

    /// <summary>
    /// Instructions for this sub-agent. Can override or append to parent agent's instructions.
    /// </summary>
    public string Instructions { get; set; } = instructions ?? throw new ArgumentNullException(nameof(instructions));

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
    public HandoffMode HandoffMode { get; set; } = handoffMode;

    /// <summary>
    /// Whether this sub-agent is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
