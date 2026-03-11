using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.SubAgents;

/// <summary>
/// Runs a sub-agent with streaming. Yields StreamUpdate for each sub-agent output.
/// Caller forwards updates to IAmbientStreamSink and aggregates text for result.
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// Runs the sub-agent. Yields updates; aggregates text for final result.
    /// </summary>
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid parentConversationId,
        Guid subAgentId,
        string identity,
        string task,
        CancellationToken cancellationToken = default);
}
