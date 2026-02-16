using SmallEBot.Core.Models;

namespace SmallEBot.Application.Streaming;

/// <summary>Receives streamed updates from the agent (text, think, tool call). Implemented by the host (e.g. Blazor writes to a channel, Cron no-ops).</summary>
public interface IStreamSink
{
    ValueTask OnNextAsync(StreamUpdate update, CancellationToken cancellationToken = default);
}
