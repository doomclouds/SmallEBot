using System.Collections.Concurrent;
using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Application.Contracts.Agents.SubAgents;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Agents.SubAgents;

/// <summary>
/// Orchestrates sub-agent execution with concurrency limits and stream forwarding.
/// Forwards sub-agent updates to the ambient stream sink and aggregates text for result.
/// </summary>
public sealed class SubAgentOrchestrator(
    ISubAgentRunner subAgentRunner,
    IAmbientStreamSink ambientStreamSink,
    IAmbientConversationId ambientConversationId)
{
    private const string DefaultExplorerIdentity =
        "Explore and gather information. Search files, read directories, run safe read-only commands. Report findings concisely.";

    private readonly SemaphoreSlim _semaphore = new(2, 2);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningAgents = new();

    /// <summary>
    /// Runs a sub-agent with the given identity and task. Streams updates to the ambient sink and returns aggregated text.
    /// </summary>
    /// <param name="identity">Sub-agent identity/persona. If null or empty, uses default explorer identity.</param>
    /// <param name="task">The task to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated text from TextStreamUpdate items.</returns>
    public async Task<string> RunAsync(
        string? identity,
        string task,
        CancellationToken cancellationToken = default)
    {
        var effectiveIdentity = string.IsNullOrWhiteSpace(identity) ? DefaultExplorerIdentity : identity;
        var conversationId = ambientConversationId.GetConversationId()
            ?? throw new InvalidOperationException("No ambient conversation id. Sub-agent must run within a conversation scope.");
        var subAgentId = Guid.NewGuid();
        var subAgentName = ExtractShortName(effectiveIdentity);

        await _semaphore.WaitAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningAgents[subAgentId] = cts;

        try
        {
            var resultBuilder = new List<string>();
            await foreach (var update in subAgentRunner.RunStreamingAsync(
                conversationId, subAgentId, effectiveIdentity, task, cts.Token))
            {
                var sink = ambientStreamSink.GetSink();
                if (sink != null)
                {
                    await sink.OnNextAsync(new SubAgentStreamUpdate(subAgentId, subAgentName, update), cts.Token);
                }

                if (update is TextStreamUpdate textUpdate)
                {
                    resultBuilder.Add(textUpdate.Text);
                }
            }

            return string.Concat(resultBuilder);
        }
        finally
        {
            _runningAgents.TryRemove(subAgentId, out _);
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Stops a running sub-agent by its id.
    /// </summary>
    public ValueTask StopAsync(Guid subAgentId)
    {
        if (_runningAgents.TryRemove(subAgentId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    private static string ExtractShortName(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return "Sub-agent";
        var trimmed = identity.Trim();
        return trimmed.Length <= 50 ? trimmed : trimmed[..50];
    }
}
