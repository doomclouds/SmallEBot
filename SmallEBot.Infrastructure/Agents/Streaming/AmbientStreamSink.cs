using SmallEBot.Application.Contracts.Agents.Streaming;

namespace SmallEBot.Infrastructure.Agents.Streaming;

/// <summary>Stores the current stream sink in AsyncLocal. Use BeginScope for explicit cleanup.</summary>
public sealed class AmbientStreamSink : IAmbientStreamSink
{
    private static readonly AsyncLocal<IStreamSink?> CurrentSink = new();

    /// <inheritdoc />
    public IStreamSink? GetSink() => CurrentSink.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(IStreamSink sink)
    {
        CurrentSink.Value = sink;
        return new Scope(() => CurrentSink.Value = null);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
