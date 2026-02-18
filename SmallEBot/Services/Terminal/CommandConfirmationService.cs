using System.Collections.Concurrent;

namespace SmallEBot.Services.Terminal;

/// <summary>Holds pending command confirmation requests by request id. Completes TCS on user action or timeout.</summary>
public sealed class CommandConfirmationService(ICommandConfirmationContext context) : ICommandConfirmationService
{
    private readonly ConcurrentDictionary<string, PendingState> _pending = new();

    /// <inheritdoc />
    public event EventHandler<PendingRequestEventArgs>? PendingRequestAdded;

    /// <inheritdoc />
    public Task<CommandConfirmResult> RequestConfirmationAsync(string command, string? workingDirectory, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var contextId = context.GetCurrentId();
        if (string.IsNullOrEmpty(contextId))
            return Task.FromResult(CommandConfirmResult.Reject);

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<CommandConfirmResult>();

        void CompleteOnce(CommandConfirmResult result)
        {
            if (!_pending.TryRemove(requestId, out var state))
                return;
            try
            {
                state.TimeoutCts.Cancel();
            }
            catch
            {
                /* ignore - may already be cancelled */
            }
            tcs.TrySetResult(result);
        }

        var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
        timeoutCts.Token.Register(() => CompleteOnce(CommandConfirmResult.Timeout));

        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => CompleteOnce(CommandConfirmResult.Reject));

        var request = new PendingCommandRequest
        {
            RequestId = requestId,
            Command = command,
            WorkingDirectory = workingDirectory
        };

        _pending[requestId] = new PendingState(tcs, timeoutCts);

        try
        {
            PendingRequestAdded?.Invoke(this, new PendingRequestEventArgs { ContextId = contextId, Request = request });
        }
        catch
        {
            CompleteOnce(CommandConfirmResult.Reject);
        }

        return tcs.Task;
    }

    /// <inheritdoc />
    public void Complete(string requestId, bool allowed)
    {
        if (!_pending.TryRemove(requestId, out var state))
            return;

        try
        {
            state.TimeoutCts.Cancel();
        }
        catch
        {
            /* ignore */
        }

        state.Tcs.TrySetResult(allowed ? CommandConfirmResult.Allow : CommandConfirmResult.Reject);
    }

    private sealed class PendingState(TaskCompletionSource<CommandConfirmResult> tcs, CancellationTokenSource timeoutCts)
    {
        public TaskCompletionSource<CommandConfirmResult> Tcs { get; } = tcs;
        public CancellationTokenSource TimeoutCts { get; } = timeoutCts;
    }
}
