using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Execution;

/// <summary>Runs the agent and yields stream updates.</summary>
public interface IAgentRunner
{
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default);

    Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default);

    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default);
}
