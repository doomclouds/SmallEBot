// SmallEBot.Domain/Agents/Services/ISubAgentRunner.cs
namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Runner interface for executing sub-agents.
/// Implementation is provided by the Application layer.
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// Runs a sub-agent in delegate mode (execute task, return result).
    /// </summary>
    /// <param name="subAgentId">The ID of the sub-agent to run.</param>
    /// <param name="task">The task description for the sub-agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the sub-agent execution.</returns>
    Task<string> RunSubAgentAsync(
        string subAgentId,
        string task,
        CancellationToken ct = default);

    /// <summary>
    /// Hands off conversation control to a sub-agent.
    /// </summary>
    /// <param name="subAgentId">The ID of the sub-agent to hand off to.</param>
    /// <param name="reason">The reason for the handoff.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandoffToSubAgentAsync(
        string subAgentId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Returns control from sub-agent to parent agent.
    /// </summary>
    /// <param name="summary">Summary of what the sub-agent accomplished.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandoffToParentAsync(
        string summary,
        CancellationToken ct = default);
}
