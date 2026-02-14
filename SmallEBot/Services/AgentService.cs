using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using SmallEBot.Data;
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
        _agent = new ChatClientAgent(chatClient,
            instructions: "You are SmallEBot, a helpful personal assistant. Be concise and friendly.",
            name: "SmallEBot");
        return _agent;
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        Guid conversationId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = GetAgent();

        // Load history and build message list for context
        var store = new ChatMessageStoreAdapter(db, conversationId);
        var history = await store.LoadMessagesAsync(ct);
        var frameworkMessages = history
            .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
            .ToList();
        frameworkMessages.Add(new ChatMessage(ChatRole.User, userMessage));

        await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, ct))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    /// <summary>Persist user and assistant messages; call after streaming completes.</summary>
    public async Task PersistMessagesAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        string assistantMessage,
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
        db.ChatMessages.Add(new Data.Entities.ChatMessage
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

        await db.SaveChangesAsync(ct);
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
