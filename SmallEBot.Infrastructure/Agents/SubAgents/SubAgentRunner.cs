using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.SubAgents;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Core.Models;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Infrastructure.Agents.SubAgents;

/// <summary>
/// Runs a sub-agent with streaming. Yields StreamUpdate for each sub-agent output.
/// Caller forwards updates to IAmbientStreamSink and aggregates text for result.
/// </summary>
public sealed class SubAgentRunner(
    IAgentBuilder agentBuilder,
    ISubAgentSessionStore subAgentSessionStore) : ISubAgentRunner
{
    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid parentConversationId,
        Guid subAgentId,
        string identity,
        string task,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetSubAgentAgentAsync(identity, cancellationToken);
        var session = await subAgentSessionStore.LoadAsync(parentConversationId, subAgentId, agent, cancellationToken)
                      ?? await agent.CreateSessionAsync(cancellationToken);

        var messages = new List<ChatMessage> { new(ChatRole.User, task) };

        var contextWindow = await agentBuilder.GetContextWindowTokensAsync(cancellationToken);
        var maxOutput = Math.Min(65536, Math.Max(8192, contextWindow / 4));
        var chatOptions = new ChatOptions { Reasoning = null, MaxOutputTokens = maxOutput };
        var runOptions = new ChatClientAgentRunOptions(chatOptions);

        var agentUpdates = agent.RunStreamingAsync(messages, session, runOptions, cancellationToken);

        await foreach (var update in ProcessStreamingUpdates(
            agentUpdates, parentConversationId, subAgentId, session, agent, cancellationToken))
        {
            yield return update;
        }
    }

    private async IAsyncEnumerable<StreamUpdate> ProcessStreamingUpdates(
        IAsyncEnumerable<AgentResponseUpdate> agentUpdates,
        Guid parentConversationId,
        Guid subAgentId,
        AIAgentSession session,
        AIAgent agent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();

        try
        {
            await foreach (var update in agentUpdates.WithCancellation(cancellationToken))
            {
                if (update.Contents is { Count: > 0 } contents)
                {
                    foreach (var content in contents)
                    {
                        switch (content)
                        {
                            case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                                yield return new TextStreamUpdate(textContent.Text);
                                break;
                            case TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text):
                                yield return new ThinkStreamUpdate(reasoningContent.Text);
                                break;
                            case FunctionCallContent fnCall:
                                var callId = fnCall.CallId;
                                toolTimers[callId] = Stopwatch.StartNew();
                                toolNames[callId] = fnCall.Name;
                                yield return new ToolCallStreamUpdate(
                                    ToolName: fnCall.Name,
                                    CallId: callId,
                                    Phase: ToolCallPhase.Started,
                                    Arguments: ToJsonString(fnCall.Arguments),
                                    Elapsed: TimeSpan.Zero);
                                break;
                            case FunctionResultContent fnResult:
                                var resCallId = fnResult.CallId;
                                if (string.IsNullOrEmpty(resCallId) && toolTimers.Count == 1)
                                    resCallId = toolTimers.Keys.First();
                                if (!string.IsNullOrEmpty(resCallId) && toolTimers.TryGetValue(resCallId, out var timer))
                                {
                                    timer.Stop();
                                    var toolName = toolNames.GetValueOrDefault(resCallId) ?? resCallId;
                                    yield return new ToolCallStreamUpdate(
                                        ToolName: toolName,
                                        CallId: resCallId,
                                        Phase: ToolCallPhase.Completed,
                                        Result: ToJsonString(fnResult.Result),
                                        Elapsed: timer.Elapsed);
                                    toolTimers.Remove(resCallId);
                                    toolNames.Remove(resCallId);
                                }
                                break;
#pragma warning disable MEAI001
                            case FunctionApprovalRequestContent approvalRequest:
                                yield return new ApprovalRequestStreamUpdate(
                                    CallId: approvalRequest.FunctionCall.CallId,
                                    ToolName: approvalRequest.FunctionCall.Name,
                                    Arguments: ToJsonString(approvalRequest.FunctionCall.Arguments),
                                    ConversationId: parentConversationId,
                                    FunctionCallId: approvalRequest.Id,
                                    RawArguments: approvalRequest.FunctionCall.Arguments);
                                break;
#pragma warning restore MEAI001
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new TextStreamUpdate(update.Text);
                }
            }
        }
        finally
        {
            await subAgentSessionStore.SaveAsync(parentConversationId, subAgentId, session, agent, CancellationToken.None);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string? ToJsonString(object? value)
    {
        if (value == null) return null;
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            try
            {
                using var doc = JsonDocument.Parse(s);
                return JsonSerializer.Serialize(doc.RootElement, JsonOptions);
            }
            catch { return s; }
        }

        if (value is System.Collections.IDictionary dict)
        {
            try { return JsonSerializer.Serialize(dict, JsonOptions); }
            catch { return SerializeDictionaryManually(dict); }
        }

        try { return JsonSerializer.Serialize(value, JsonOptions); }
        catch { return value.ToString(); }
    }

    private static string SerializeDictionaryManually(System.Collections.IDictionary dict)
    {
        var entries = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = entry.Key.ToString() ?? "null";
            var val = entry.Value switch
            {
                null => "null",
                string str => $"\"{str.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
                bool b => b.ToString().ToLower(),
                int or long or double or float or decimal => entry.Value.ToString(),
                _ => JsonSerializer.Serialize(entry.Value, JsonOptions)
            };
            entries.Add($"\"{key}\": {val}");
        }
        return "{\n  " + string.Join(",\n  ", entries) + "\n}";
    }
}
