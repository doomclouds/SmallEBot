using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
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
    private List<IAsyncDisposable>? _mcpClients;

    private async Task<AIAgent> EnsureAgentAsync(CancellationToken ct)
    {
        if (_agent != null) return _agent;

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? Environment.GetEnvironmentVariable("DeepseekKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            log.LogWarning("ANTHROPIC_API_KEY or DeepseekKey environment variable is not set.");
        }

        var baseUrl = config["Anthropic:BaseUrl"] ?? config["DeepSeek:AnthropicBaseUrl"] ?? "https://api.deepseek.com/anthropic";
        var model = config["Anthropic:Model"] ?? config["DeepSeek:Model"] ?? "deepseek-chat";

        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey ?? "");
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", baseUrl);
        var anthropicClient = new AnthropicClient();

        var tools = new List<AITool> { AIFunctionFactory.Create(GetCurrentTime) };
        _mcpClients = [];

        var mcpSection = config.GetSection("mcpServers");
        if (mcpSection.Exists())
        {
            foreach (var child in mcpSection.GetChildren())
            {
                var type = child["type"]?.ToString();
                var command = child["command"]?.ToString();

                if (type == "stdio" || (string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(command)))
                {
                    if (string.IsNullOrEmpty(command))
                    {
                        log.LogWarning("MCP stdio server '{Name}' has no command, skipped.", child.Key);
                        continue;
                    }
                    var argsSection = child.GetSection("args");
                    var arguments = argsSection.GetChildren()
                        .OrderBy(c => int.TryParse(c.Key, out var i) ? i : 0)
                        .Select(c => c.Value ?? "")
                        .ToArray();
                    var env = child.GetSection("env").Get<Dictionary<string, string?>>() ?? new Dictionary<string, string?>();
                    try
                    {
                        var transport = new StdioClientTransport(new StdioClientTransportOptions
                        {
                            Name = child.Key,
                            Command = command,
                            Arguments = arguments,
                            EnvironmentVariables = env
                        });
                        var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                        _mcpClients.Add(mcpClient);
                        var mcpTools = await mcpClient.ListToolsAsync();
                        tools.AddRange(mcpTools.Cast<AITool>());
                        log.LogInformation("MCP stdio server '{Name}' ({Command}) loaded with {Count} tools.", child.Key, command, mcpTools.Count);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Failed to load MCP stdio server '{Name}' (command: {Command}), skipping.", child.Key, command);
                    }
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
                    try
                    {
                        var transport = new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Endpoint = new Uri(url),
                            TransportMode = HttpTransportMode.AutoDetect,
                            ConnectionTimeout = TimeSpan.FromSeconds(30)
                        });
                        var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                        _mcpClients.Add(mcpClient);
                        var mcpTools = await mcpClient.ListToolsAsync();
                        tools.AddRange(mcpTools.Cast<AITool>());
                        log.LogInformation("MCP http server '{Name}' at {Url} loaded with {Count} tools.", child.Key, url, mcpTools.Count);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Failed to load MCP http server '{Name}' at {Url}, skipping.", child.Key, url);
                    }
                }
            }
        }

        _agent = anthropicClient.AsAIAgent(
            model: model,
            name: "SmallEBot",
            instructions: "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.",
            tools: tools);
        return _agent;
    }

    [Description("Get the current UTC date and time in ISO 8601 format.")]
    private static string GetCurrentTime() => DateTime.UtcNow.ToString("O");

    public async IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = await EnsureAgentAsync(ct);

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
                        case TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text):
                            yield return new ThinkStreamUpdate(reasoningContent.Text);
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

    /// <summary>Persist user message and ordered assistant segments (text and tool calls) with CreatedAt for timeline.</summary>
    public async Task PersistMessagesAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        IReadOnlyList<AssistantSegment> assistantSegments,
        CancellationToken ct = default)
    {
        var conv = await db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) return;

        var msgCountBefore = await convSvc.GetMessageCountAsync(conversationId, ct);
        var baseTime = DateTime.UtcNow;

        db.ChatMessages.Add(new Data.Entities.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CreatedAt = baseTime
        });
        baseTime = baseTime.AddMilliseconds(1);

        var toolOrder = 0;
        foreach (var seg in assistantSegments)
        {
            if (seg.IsText && !string.IsNullOrEmpty(seg.Text))
            {
                db.ChatMessages.Add(new Data.Entities.ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = seg.Text,
                    CreatedAt = baseTime
                });
            }
            else if (!seg.IsText)
            {
                db.ToolCalls.Add(new ToolCall
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversationId,
                    ToolName = seg.ToolName ?? "",
                    Arguments = seg.Arguments,
                    Result = seg.Result,
                    SortOrder = toolOrder++,
                    CreatedAt = baseTime
                });
            }
            baseTime = baseTime.AddMilliseconds(1);
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
        var agent = await EnsureAgentAsync(ct);
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
