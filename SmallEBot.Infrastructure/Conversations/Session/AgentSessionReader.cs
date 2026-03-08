using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Conversations.Session;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Infrastructure.Conversations.Session;

/// <summary>
/// Reads message history from serialized AgentSession data.
/// Parses JSON structure from session.json to extract ChatMessage objects.
/// </summary>
public sealed class AgentSessionReader(
    IAgentSessionStore sessionStore,
    ILogger<AgentSessionReader> logger) : IAgentSessionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var json = await sessionStore.GetSessionJsonAsync(conversationId, ct);
        if (string.IsNullOrEmpty(json))
        {
            logger.LogDebug("No session data found for conversation {ConversationId}", conversationId);
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseMessages(doc.RootElement);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse session data for conversation {ConversationId}", conversationId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int firstMessageIndex,
        CancellationToken ct = default)
    {
        var messages = await GetMessagesAsync(conversationId, ct);
        if (messages.Count == 0) return null;

        if (firstMessageIndex < 0 || firstMessageIndex >= messages.Count) return null;

        var message = messages[firstMessageIndex];
        if (message.Role != ChatRole.User) return null;

        // Extract text content
        var textContent = message.Contents
            .OfType<TextContent>()
            .FirstOrDefault();

        return textContent?.Text;
    }

    private List<ChatMessage> ParseMessages(JsonElement sessionData)
    {
        var messages = new List<ChatMessage>();

        // Navigate to stateBag.InMemoryChatHistoryProvider.messages
        if (!sessionData.TryGetProperty("stateBag", out var stateBag))
        {
            logger.LogDebug("SessionData missing stateBag property");
            return messages;
        }

        if (!stateBag.TryGetProperty("InMemoryChatHistoryProvider", out var historyProvider))
        {
            logger.LogDebug("SessionData stateBag missing InMemoryChatHistoryProvider");
            return messages;
        }

        if (!historyProvider.TryGetProperty("messages", out var messagesArray) ||
            messagesArray.ValueKind != JsonValueKind.Array)
        {
            logger.LogDebug("InMemoryChatHistoryProvider missing messages array");
            return messages;
        }

        foreach (var messageElement in messagesArray.EnumerateArray())
        {
            var message = ParseMessage(messageElement);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    private ChatMessage? ParseMessage(JsonElement element)
    {
        if (!element.TryGetProperty("role", out var roleElement))
        {
            return null;
        }

        var roleString = roleElement.GetString();
        var role = ParseRole(roleString);
        if (role == null)
        {
            logger.LogDebug("Unknown role: {Role}", roleString);
            return null;
        }

        var contents = new List<AIContent>();

        if (element.TryGetProperty("contents", out var contentsArray) &&
            contentsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentElement in contentsArray.EnumerateArray())
            {
                var content = ParseContent(contentElement);
                if (content != null)
                {
                    contents.Add(content);
                }
            }
        }

        // Extract optional metadata
        string? authorName = null;
        if (element.TryGetProperty("authorName", out var authorElement))
        {
            authorName = authorElement.GetString();
        }

        var message = new ChatMessage(role.Value, contents);

        // Set additional properties if available
        if (!string.IsNullOrEmpty(authorName))
        {
            message.AuthorName = authorName;
        }

        return message;
    }

    private ChatRole? ParseRole(string? role)
    {
        return role?.ToLowerInvariant() switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => null
        };
    }

    private AIContent? ParseContent(JsonElement element)
    {
        if (!element.TryGetProperty("$type", out var typeElement))
        {
            return null;
        }

        var type = typeElement.GetString()?.ToLowerInvariant();

        return type switch
        {
            "text" => ParseTextContent(element),
            "reasoning" => ParseReasoningContent(element),
            "functioncall" => ParseFunctionCallContent(element),
            "functionresult" => ParseFunctionResultContent(element),
            _ => null
        };
    }

    private TextContent? ParseTextContent(JsonElement element)
    {
        if (!element.TryGetProperty("text", out var textElement))
        {
            return null;
        }

        var text = textElement.GetString();
        return string.IsNullOrEmpty(text) ? null : new TextContent(text);
    }

    private TextReasoningContent? ParseReasoningContent(JsonElement element)
    {
        if (!element.TryGetProperty("text", out var textElement))
        {
            return null;
        }

        var text = textElement.GetString();
        return string.IsNullOrEmpty(text) ? null : new TextReasoningContent(text);
    }

    private FunctionCallContent? ParseFunctionCallContent(JsonElement element)
    {
        var callId = element.TryGetProperty("callId", out var callIdElement)
            ? callIdElement.GetString()
            : null;

        if (!element.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        var name = nameElement.GetString();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Parse arguments as dictionary
        IDictionary<string, object?>? args = null;
        if (element.TryGetProperty("arguments", out var argsElement) &&
            argsElement.ValueKind == JsonValueKind.Object)
        {
            args = new Dictionary<string, object?>();
            foreach (var prop in argsElement.EnumerateObject())
            {
                args[prop.Name] = ParseJsonValue(prop.Value);
            }
        }

        return new FunctionCallContent(callId ?? string.Empty, name, args);
    }

    private FunctionResultContent? ParseFunctionResultContent(JsonElement element)
    {
        var callId = element.TryGetProperty("callId", out var callIdElement)
            ? callIdElement.GetString()
            : null;

        if (!element.TryGetProperty("result", out var resultElement))
        {
            return null;
        }

        var result = ParseJsonValue(resultElement);

        return new FunctionResultContent(callId ?? string.Empty, result);
    }

    private static object? ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => element.Deserialize<Dictionary<string, object?>>(JsonOptions),
            JsonValueKind.Array => element.Deserialize<List<object?>>(JsonOptions),
            _ => element.ToString()
        };
    }
}
