using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Core.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Application.Agents.Execution;

/// <summary>
/// Runs the agent, maps updates to StreamUpdate, and persists session on completion or cancellation.
/// </summary>
public sealed class AgentRunner(
    IAgentBuilder agentBuilder,
    IAgentSessionStore sessionStore) : IAgentRunner
{
    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);
        var session = await sessionStore.LoadAsync(conversationId, agent, cancellationToken)
                      ?? await agent.CreateSessionAsync(cancellationToken);

        var messages = new List<ChatMessage> { new(ChatRole.User, userMessage) };

        var reasoningOpt = new ReasoningOptions();
        if (useThinking)
        {
            reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
            reasoningOpt.Output = ReasoningOutput.Full;
        }
        var chatOptions = new ChatOptions { Reasoning = useThinking ? reasoningOpt : null };
        var runOptions = new ChatClientAgentRunOptions(chatOptions);

        var agentUpdates = agent.RunStreamingAsync(messages, session, runOptions, cancellationToken);

        await foreach (var update in ProcessStreamingUpdates(agentUpdates, conversationId, agent, session, cancellationToken))
        {
            yield return update;
        }
    }

    public async Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, cancellationToken);
        await sessionStore.TruncateFromIndexAsync(conversationId, messageIndex, agent, cancellationToken);
    }

    public async IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason,
        IDictionary<string, object?>? rawArguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, cancellationToken);
        var session = await sessionStore.LoadAsync(conversationId, agent, cancellationToken)
                      ?? await agent.CreateSessionAsync(cancellationToken);

        var messages = new List<ChatMessage>();

#pragma warning disable MEAI001
        var functionCall = new FunctionCallContent(
            callId: functionCallId,
            name: functionName,
            arguments: rawArguments);
        var approvalContent = new FunctionApprovalResponseContent(
            id: approvalRequestId,
            approved: approved,
            functionCall: functionCall)
        {
            Reason = reason
        };
#pragma warning restore MEAI001
        messages.Add(new ChatMessage(ChatRole.User, [approvalContent]));

        var chatOptions = new ChatOptions { Reasoning = null };
        var runOptions = new ChatClientAgentRunOptions(chatOptions);

        var agentUpdates = agent.RunStreamingAsync(messages, session, runOptions, cancellationToken);

        await foreach (var update in ProcessStreamingUpdates(agentUpdates, conversationId, agent, session, cancellationToken))
        {
            yield return update;
        }
    }

    private async IAsyncEnumerable<StreamUpdate> ProcessStreamingUpdates(
        IAsyncEnumerable<AgentResponseUpdate> agentUpdates,
        Guid conversationId,
        AIAgent agent,
        AgentSession session,
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
                                    ConversationId: conversationId,
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
            await sessionStore.SaveAsync(conversationId, session, agent, CancellationToken.None);
        }
    }

    public async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, cancellationToken);
        var prompt = $"Generate a very short title (under 20 chars, no quotes) for a conversation that starts with: {firstMessage}";
        var titleOptions = new ChatClientAgentRunOptions(new ChatOptions { Reasoning = null });
        try
        {
            var result = await agent.RunAsync(prompt, null, titleOptions, cancellationToken);
            var t = result.Text.Trim();
            return string.IsNullOrEmpty(t) ? "New conversation" : t;
        }
        catch
        {
            return firstMessage.Length > 20 ? firstMessage[..20] + "…" : firstMessage;
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
