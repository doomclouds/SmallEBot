using System.ClientModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using SmallEBot.Data;
using SmallEBot.Data.Entities;
using SmallEBot.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services;

public class AgentService(
    AppDbContext db,
    ConversationService convSvc,
    IConfiguration config,
    ILogger<AgentService> log)
{
    private AIAgent? _agent;

    private AIAgent GetAgent()
    {
        if (_agent != null) return _agent;

        var apiKey = Environment.GetEnvironmentVariable("DeepseekKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            log.LogWarning("DeepseekKey environment variable is not set.");
        }

        var baseUrl = config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
        var model = config["DeepSeek:Model"] ?? "deepseek-chat";

        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey ?? ""), options);
        var chatClient = client.GetChatClient(model).AsIChatClient();

        var tools = new List<AITool> { AIFunctionFactory.Create(GetCurrentTime) };
        var mcpSection = config.GetSection("mcpServers");
        if (mcpSection.Exists())
        {
            foreach (var child in mcpSection.GetChildren())
            {
                var type = child["type"]?.ToString();
                if (type == "stdio")
                {
                    log.LogInformation("MCP stdio server '{Name}' configured but not loaded in this phase.", child.Key);
                    continue;
                }
                if (type == "http")
                {
                    var url = child["url"]?.ToString();
                    if (string.IsNullOrEmpty(url))
                    {
                        log.LogWarning("MCP http server '{Name}' has no url, skipped.", child.Key);
                        continue;
                    }
                    log.LogInformation("MCP http server '{Name}' at {Url} configured (tool loading requires async init, skipped in this phase).", child.Key, url);
                }
            }
        }

        _agent = new ChatClientAgent(
            chatClient,
            instructions: "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool.",
            name: "SmallEBot",
            description: null,
            tools: tools,
            loggerFactory: null,
            services: null);
        return _agent;
    }

    [Description("Get the current UTC date and time in ISO 8601 format.")]
    private static string GetCurrentTime() => DateTime.UtcNow.ToString("O");

    public async IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(
        Guid conversationId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = GetAgent();

        var store = new ChatMessageStoreAdapter(db, conversationId);
        var history = await store.LoadMessagesAsync(ct);
        var frameworkMessages = history
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, ct))
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
                        case FunctionCallContent fnCall:
                            yield return new ToolCallStreamUpdate(fnCall.Name, ToJsonString(fnCall.Arguments));
                            break;
                        case FunctionResultContent fnResult:
                            yield return new ToolCallStreamUpdate(fnResult.CallId ?? "result", Result: ToJsonString(fnResult.Result));
                            break;
                        default:
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

    /// <summary>Persist user and assistant messages and optional tool calls; call after streaming completes.</summary>
    public async Task PersistMessagesAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        string assistantMessage,
        IReadOnlyList<(string ToolName, string? Arguments, string? Result)>? toolCalls = null,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) return;

        var msgCountBefore = await convSvc.GetMessageCountAsync(conversationId, ct);

        db.ChatMessages.Add(new Data.Entities.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CreatedAt = DateTime.UtcNow
        });

        var assistantMsgId = Guid.NewGuid();
        var assistantMsg = new Data.Entities.ChatMessage
        {
            Id = assistantMsgId,
            ConversationId = conversationId,
            Role = "assistant",
            Content = assistantMessage,
            CreatedAt = DateTime.UtcNow
        };
        db.ChatMessages.Add(assistantMsg);

        if (toolCalls is { Count: > 0 })
        {
            for (var i = 0; i < toolCalls.Count; i++)
            {
                var tc = toolCalls[i];
                db.ToolCalls.Add(new ToolCall
                {
                    Id = Guid.NewGuid(),
                    ChatMessageId = assistantMsgId,
                    ToolName = tc.ToolName ?? "",
                    Arguments = tc.Arguments,
                    Result = tc.Result,
                    SortOrder = i
                });
            }
        }

        conv.UpdatedAt = DateTime.UtcNow;

        if (msgCountBefore == 0)
        {
            var title = await GenerateTitleAsync(userMessage, ct);
            conv.Title = title;
        }

        await db.SaveChangesAsync(ct);
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

    private static ChatRole ToChatRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User
    };

    private async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken ct = default)
    {
        var agent = GetAgent();
        var prompt = $"Generate a very short title (under 20 chars, no quotes) for a conversation that starts with: {firstMessage}";
        try
        {
            var result = await agent.RunAsync(prompt, null, null, ct);
            var t = result.Text.Trim();
            if (t.Length > 20) t = t[..20];
            return string.IsNullOrEmpty(t) ? "新对话" : t;
        }
        catch
        {
            return firstMessage.Length > 20 ? firstMessage[..20] + "…" : firstMessage;
        }
    }
}
