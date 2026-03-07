// SmallEBot.Domain/Agents/IAgentConfigRepository.cs
namespace SmallEBot.Domain.Agents;

/// <summary>
/// Repository interface for agent configurations.
/// </summary>
public interface IAgentConfigRepository
{
    /// <summary>
    /// Gets the default agent configuration.
    /// </summary>
    Task<AgentConfig?> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets an agent configuration by ID.
    /// </summary>
    Task<AgentConfig?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Gets all agent configurations.
    /// </summary>
    Task<IReadOnlyList<AgentConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves an agent configuration.
    /// </summary>
    Task SaveAsync(AgentConfig agent, CancellationToken ct = default);

    /// <summary>
    /// Deletes an agent configuration by ID.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Sets the default agent by ID.
    /// </summary>
    Task SetDefaultAsync(string id, CancellationToken ct = default);
}
