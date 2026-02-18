namespace SmallEBot.Services.Terminal;

/// <summary>Manages pending command confirmation requests. The tool awaits RequestConfirmationAsync; the UI calls Complete on Allow/Reject.</summary>
public interface ICommandConfirmationService
{
    /// <summary>Raised when a new confirmation request is added. Args include ContextId and PendingCommandRequest.</summary>
    event EventHandler<PendingRequestEventArgs>? PendingRequestAdded;

    /// <summary>Registers a pending request and returns a task that completes when the user Allows, Rejects, or timeout occurs. Returns Reject immediately if context id is not set.</summary>
    Task<CommandConfirmResult> RequestConfirmationAsync(string command, string? workingDirectory, int timeoutSeconds, CancellationToken cancellationToken = default);

    /// <summary>Completes the pending request with the given result. Called by the UI when the user clicks Allow or Reject.</summary>
    void Complete(string requestId, bool allowed);
}
