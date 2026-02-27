using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Context;
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using SmallEBot.Services.Conversation;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services.Agent;

/// <summary>Host implementation of IAgentRunner: loads history from repository, uses IAgentBuilder to run the agent and map updates to StreamUpdate.</summary>
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager,
    IAgentConfigService agentConfig) : IAgentRunner
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

        // Get conversation to check CompressedAt for filtering
        var conversation = await conversationRepository.GetByIdNoUserCheckAsync(conversationId, cancellationToken);

        // Load all history and filter by CompressedAt
        var allHistory = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var allToolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, cancellationToken);

        // Filter history by CompressedAt - only send messages after compression timestamp to LLM
        var history = conversation?.CompressedAt != null
            ? allHistory.Where(m => m.CreatedAt > conversation.CompressedAt.Value).ToList()
            : allHistory;

        var toolCalls = conversation?.CompressedAt != null
            ? allToolCalls.Where(t => t.CreatedAt > conversation.CompressedAt.Value).ToList()
            : allToolCalls;

        var toolResultMaxLength = agentConfig.GetToolResultMaxLength();
        var toolCallsByTurn = toolCalls
            .Where(tc => tc.TurnId != null)
            .GroupBy(tc => tc.TurnId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Core.Entities.ToolCall>)g.OrderBy(tc => tc.SortOrder).ToList());
        var maxInputTokens = (int)(await agentBuilder.GetContextWindowTokensAsync(cancellationToken) * 0.8);
        var coreMessages = history.ToList();
        var trimResult = contextWindowManager.TrimToFit(coreMessages, maxInputTokens);
        var frameworkMessages = new List<ChatMessage>();
        foreach (var m in trimResult.Messages)
        {
            var content = m.Content ?? "";
            // Append tool summaries to assistant messages
            if (m.Role == "assistant" && m.TurnId != null && toolCallsByTurn.TryGetValue(m.TurnId.Value, out var turnToolCalls))
            {
                content += BuildToolSummary(turnToolCalls, toolResultMaxLength);
            }
            frameworkMessages.Add(new ChatMessage(ToChatRole(m.Role), content));
        }

        var hasAttachments = (attachedPaths?.Count ?? 0) + (requestedSkillIds?.Count ?? 0) > 0;
        if (hasAttachments)
        {
            var fragment = await fragmentBuilder.BuildFragmentAsync(
                attachedPaths ?? [],
                requestedSkillIds ?? [],
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                frameworkMessages.Add(new ChatMessage(ChatRole.User, fragment));
            }
        }
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        var reasoningOpt = new ReasoningOptions();
        if(useThinking)
        {
            reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
            reasoningOpt.Output = ReasoningOutput.Full;
        }
        else
        {
            reasoningOpt.Effort = null;
            reasoningOpt.Output = null;
        }
        var chatOptions = new ChatOptions
        {
            Reasoning = reasoningOpt
        };
        var runOptions = new ChatClientAgentRunOptions(chatOptions);
        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, runOptions, cancellationToken))
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
                            var callId = fnCall.CallId ?? Guid.NewGuid().ToString("N");
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

    private static ChatRole ToChatRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User
    };

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

    private static string TruncateToolResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    /// <summary>Parses JSON arguments and formats as compact key=value pairs to save tokens.</summary>
    private static string FormatArgumentsCompact(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return "";
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            var props = doc.RootElement.EnumerateObject()
                .Select(p => $"{p.Name}={FormatValueCompact(p.Value)}")
                .ToArray();
            return string.Join(", ", props);
        }
        catch
        {
            // Fallback: just show first 50 chars
            return arguments.Length <= 50 ? arguments : arguments[..50] + "...";
        }
    }

    private static string FormatValueCompact(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"\"{value.GetString()}\"",
        JsonValueKind.Number => value.ToString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => $"[{value.GetArrayLength()} items]",
        JsonValueKind.Object => "{...}",
        _ => value.ToString() ?? ""
    };

    /// <summary>Builds compact tool summary: [Tool: Name(key=val, ...)] → result</summary>
    private static string BuildToolSummary(IReadOnlyList<Core.Entities.ToolCall> toolCalls, int resultMaxLength)
    {
        if (toolCalls.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        foreach (var tc in toolCalls)
        {
            var args = FormatArgumentsCompact(tc.Arguments);
            var result = TruncateToolResult(tc.Result, resultMaxLength);
            sb.AppendLine($"[Tool: {tc.ToolName}({args})] → {result}");
        }
        return sb.ToString();
    }
}
