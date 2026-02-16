using System.Threading.Channels;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Services;

/// <summary>IStreamSink implementation that writes each StreamUpdate to a ChannelWriter (e.g. for Blazor to read and display).</summary>
public sealed class ChannelStreamSink(ChannelWriter<StreamUpdate> writer) : IStreamSink
{
    public ValueTask OnNextAsync(StreamUpdate update, CancellationToken cancellationToken = default) =>
        writer.WriteAsync(update, cancellationToken);
}
