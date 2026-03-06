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
    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        bool useThinking = false,
        CancellationToken cancellationToken = default);
}
