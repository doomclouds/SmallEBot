using SmallEBot.Core.Models;

namespace SmallEBot.Application.Streaming;

/// <summary>Runs the agent and yields stream updates. Implemented by the host (uses IAgentBuilder, MCP, etc.).</summary>
public interface IAgentRunner
{
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Generate a short title for a conversation from its first message. Used when message count is 0.</summary>
    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    /// <summary>Continue streaming after user approval/rejection of a tool call.</summary>
    /// <param name="conversationId">The conversation ID</param>
    /// <param name="functionCallId">The FunctionCallContent.CallId (links the approval response to the original call)</param>
    /// <param name="functionName">The original function name being approved</param>
    /// <param name="approvalRequestId">The FunctionApprovalRequestContent.Id</param>
    /// <param name="approved">Whether the call is approved</param>
    /// <param name="reason">Optional reason for rejection</param>
    /// <param name="rawArguments">The original function arguments (required for execution)</param>
    /// <param name="cancellationToken">Cancellation token</param>
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
