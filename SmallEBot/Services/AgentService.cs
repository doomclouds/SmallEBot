using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using SmallEBot.Data;
using SmallEBot.Data.Entities;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SmallEBot.Services;

public class AgentService
{
    private readonly AppDbContext _db;
    private readonly ConversationService _convSvc;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentService> _log;
    private AIAgent? _agent;

    public AgentService(
        AppDbContext db,
        ConversationService convSvc,
        IConfiguration config,
        ILogger<AgentService> log)
    {
        _db = db;
        _convSvc = convSvc;
        _config = config;
        _log = log;
    }

    private AIAgent GetAgent()
    {
        if (_agent != null) return _agent;

        var apiKey = Environment.GetEnvironmentVariable("DeepseekKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            _log.LogWarning("DeepseekKey environment variable is not set.");
        }

        var baseUrl = _config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
        var model = _config["DeepSeek:Model"] ?? "deepseek-chat";

        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey ?? ""), options);
        var chatClient = client.GetChatClient(model);
        var ichatClient = chatClient.AsIChatClient();
        _agent = new ChatClientAgent(ichatClient,
            instructions: "You are SmallEBot, a helpful personal assistant. Be concise and friendly.",
            name: "SmallEBot");
        return _agent;
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = GetAgent();
        var fullText = "";

        // Load history and build message list for context
        var store = new ChatMessageStoreAdapter(_db, conversationId);
        var history = await store.LoadMessagesAsync(ct);
        var frameworkMessages = history
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, ct))
        {
            var text = update?.Text ?? update?.ToString() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                fullText += text;
                yield return text;
            }
        }
        // Caller must call PersistMessagesAsync(conversationId, userName, userMessage, fullText) after stream completes
    }

    /// <summary>Persist user and assistant messages; call after streaming completes.</summary>
    public async Task PersistMessagesAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        string assistantMessage,
        CancellationToken ct = default)
    {
        var conv = await _db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) return;

        var msgCountBefore = await _convSvc.GetMessageCountAsync(conversationId, ct);

        _db.ChatMessages.Add(new Data.Entities.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CreatedAt = DateTime.UtcNow
        });
        _db.ChatMessages.Add(new Data.Entities.ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = assistantMessage,
            CreatedAt = DateTime.UtcNow
        });
        conv.UpdatedAt = DateTime.UtcNow;

        if (msgCountBefore == 0)
        {
            var title = await GenerateTitleAsync(userMessage, ct);
            conv.Title = title;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static ChatRole ToChatRole(string role) => role?.ToLowerInvariant() switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User
    };

    public async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken ct = default)
    {
        var agent = GetAgent();
        var prompt = $"Generate a very short title (under 20 chars, no quotes) for a conversation that starts with: {firstMessage}";
        try
        {
            var result = await agent.RunAsync(prompt, null, null, ct);
            var t = (result?.Text ?? result?.ToString() ?? "").Trim();
            if (t.Length > 20) t = t[..20];
            return string.IsNullOrEmpty(t) ? "新对话" : t;
        }
        catch
        {
            return firstMessage.Length > 20 ? firstMessage[..20] + "…" : (firstMessage ?? "新对话");
        }
    }
}
