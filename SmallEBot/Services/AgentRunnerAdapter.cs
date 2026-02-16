using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services;

/// <summary>Host implementation of IAgentRunner: loads history from repository, uses IAgentBuilder to run the agent and map updates to StreamUpdate.</summary>
public sealed class AgentRunnerAdapter(IConversationRepository conversationRepository, IAgentBuilder agentBuilder) : IAgentRunner
{
    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);
        var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var frameworkMessages = history
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, cancellationToken))
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
        try
        {
            var result = await agent.RunAsync(prompt, null, null, cancellationToken);
            var t = result.Text.Trim();
            if (t.Length > 20) t = t[..20];
            return string.IsNullOrEmpty(t) ? "新对话" : t;
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
