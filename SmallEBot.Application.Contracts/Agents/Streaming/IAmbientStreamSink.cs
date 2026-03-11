namespace SmallEBot.Application.Contracts.Agents.Streaming;

/// <summary>
/// Request-scoped stream sink for pushing updates (e.g. sub-agent stream) to the current conversation's channel.
/// Set via BeginScope at the start of StreamResponseAsync. Tools can inject to forward sub-agent updates.
/// </summary>
public interface IAmbientStreamSink
{
    /// <summary>Returns the current request's sink, or null if not in a streaming context.</summary>
    IStreamSink? GetSink();

    /// <summary>Sets the sink for the current async context. Returns disposable to clear.</summary>
    IDisposable BeginScope(IStreamSink sink);
}
