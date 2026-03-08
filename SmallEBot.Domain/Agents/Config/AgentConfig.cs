using SmallEBot.Domain.Agents.Config.ValueObjects;
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Agents.Config;

/// <summary>
/// Aggregate root for agent configuration.
/// Contains all static configuration for an AI agent, including sub-agents.
/// </summary>
public class AgentConfig(
    string id,
    string name,
    string description,
    string instructions,
    string modelId)
    : IAggregateRoot, IEntity<string>
{
    public string Id { get; init; } = id ?? throw new ArgumentNullException(nameof(id));
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string Description { get; set; } = description ?? throw new ArgumentNullException(nameof(description));

    /// <summary>
    /// System prompt instructions for this agent.
    /// </summary>
    public string Instructions { get; set; } = instructions ?? throw new ArgumentNullException(nameof(instructions));

    /// <summary>
    /// The model ID to use. References a model configuration by ID.
    /// </summary>
    public string ModelId { get; set; } = modelId ?? throw new ArgumentNullException(nameof(modelId));

    /// <summary>
    /// Tool set available to this agent.
    /// </summary>
    public ToolSet Tools { get; set; } = ToolSet.Full;

    /// <summary>
    /// MCP server IDs to enable for this agent.
    /// </summary>
    public string[] McpServerIds { get; set; } = [];

    /// <summary>
    /// Skill IDs to enable for this agent. Supports wildcards.
    /// </summary>
    public string[] SkillIds { get; set; } = ["*"];

    /// <summary>
    /// Terminal configuration for shell command execution.
    /// </summary>
    public TerminalConfig Terminal { get; set; } = TerminalConfig.Default;

    /// <summary>
    /// Sub-agent configurations.
    /// </summary>
    private readonly List<SubAgentConfig> _subAgents = [];
    public IReadOnlyList<SubAgentConfig> SubAgents => _subAgents.AsReadOnly();

    /// <summary>
    /// Whether this is the default agent.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Adds a sub-agent configuration.
    /// </summary>
    public void AddSubAgent(SubAgentConfig subAgent)
    {
        ArgumentNullException.ThrowIfNull(subAgent);
        if (_subAgents.Any(sa => sa.Id == subAgent.Id))
            throw new InvalidOperationException($"Sub-agent with ID '{subAgent.Id}' already exists.");
        _subAgents.Add(subAgent);
    }

    /// <summary>
    /// Removes a sub-agent configuration.
    /// </summary>
    public void RemoveSubAgent(string subAgentId)
    {
        var subAgent = _subAgents.FirstOrDefault(sa => sa.Id == subAgentId);
        if (subAgent != null)
            _subAgents.Remove(subAgent);
    }

    /// <summary>
    /// Gets a sub-agent by ID.
    /// </summary>
    public SubAgentConfig? GetSubAgent(string subAgentId) =>
        _subAgents.FirstOrDefault(sa => sa.Id == subAgentId);
}
