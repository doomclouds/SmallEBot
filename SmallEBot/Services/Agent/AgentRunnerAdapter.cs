using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Services.Conversation;
using SmallEBot.Services.Session;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services.Agent;

/// <summary>
/// Host implementation of IAgentRunner: uses ISessionManager to manage AgentSession,
/// runs the agent, and maps updates to StreamUpdate.
/// </summary>
public sealed class AgentRunnerAdapter(
    IAgentBuilder agentBuilder,
    ISessionManager sessionManager,
    ITurnContextFragmentBuilder fragmentBuilder) : IAgentRunner
{
    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);

        // Get or create session (uses AgentSession instead of loading history from repository)
        var (session, _) = await sessionManager.GetOrCreateSessionAsync(
            conversationId,
            "user", // TODO: Get from context
            agent,
            cancellationToken);

        // Build attachments fragment if any
        var messages = new List<ChatMessage>();
        var hasAttachments = (attachedPaths?.Count ?? 0) + (requestedSkillIds?.Count ?? 0) > 0;
        if (hasAttachments)
        {
            var fragment = await fragmentBuilder.BuildFragmentAsync(
                attachedPaths ?? [],
                requestedSkillIds ?? [],
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                messages.Add(new ChatMessage(ChatRole.User, fragment));
            }
        }
        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        // Configure reasoning
        var reasoningOpt = new ReasoningOptions();
        if (useThinking)
        {
            reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
            reasoningOpt.Output = ReasoningOutput.Full;
        }
        var chatOptions = new ChatOptions { Reasoning = useThinking ? reasoningOpt : null };
        var runOptions = new ChatClientAgentRunOptions(chatOptions);

        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();

        // Run with session (session maintains history internally)
        await foreach (var update in agent.RunStreamingAsync(messages, session, runOptions, cancellationToken))
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
                    }
                }
            }
            else if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextStreamUpdate(update.Text);
            }
        }

        // Persist session after completion
        await sessionManager.PersistSessionAsync(conversationId, session, agent, cancellationToken);
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

    private static string? ToJsonString(object? value)
    {
        if (value == null) return null;
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            try
            {
                using var doc = JsonDocument.Parse(s);
                return JsonSerializer.Serialize(doc.RootElement, jsonOptions);
            }
            catch { return s; }
        }
        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), jsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }
}
