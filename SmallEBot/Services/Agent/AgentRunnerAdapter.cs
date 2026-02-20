using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Context;
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
    IContextWindowManager contextWindowManager) : IAgentRunner
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
        var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var maxInputTokens = (int)(agentBuilder.GetContextWindowTokens() * 0.8);
        var coreMessages = history.ToList();
        var trimResult = contextWindowManager.TrimToFit(coreMessages, maxInputTokens);
        var frameworkMessages = trimResult.Messages
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();

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
                            yield return new ToolCallStreamUpdate(fnCall.Name, ToJsonString(fnCall.Arguments));
                            break;
                        case FunctionResultContent fnResult:
                            yield return new ToolCallStreamUpdate(fnResult.CallId, Result: ToJsonString(fnResult.Result));
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
}
