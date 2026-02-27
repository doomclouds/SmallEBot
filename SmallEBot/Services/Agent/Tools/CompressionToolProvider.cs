using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides context compression tool for LLM to manually compress conversation history.</summary>
public sealed class CompressionToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository,
    IAgentConfigService agentConfig,
    ICompressionService compressionService) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "Compression";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(CompactContext);
    }

    [Description("Compress conversation history into a summary to save context space. Use when context is running low or user requests /compact. Returns JSON with success, message, and compressedCount.")]
    private async Task<string> CompactContext(CancellationToken ct = default)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return JsonSerializer.Serialize(new { success = false, message = "No active conversation", compressedCount = 0 }, JsonOptions);

        var conversation = await repository.GetByIdAsync(conversationId.Value, "", ct);
        if (conversation == null)
            return JsonSerializer.Serialize(new { success = false, message = "Conversation not found", compressedCount = 0 }, JsonOptions);

        var allMessages = await repository.GetMessagesForConversationAsync(conversationId.Value, ct);
        var messagesToCompress = conversation.CompressedAt == null
            ? allMessages.ToList()
            : allMessages.Where(m => m.CreatedAt <= conversation.CompressedAt.Value).ToList();

        if (messagesToCompress.Count == 0)
            return JsonSerializer.Serialize(new { success = false, message = "No messages to compress", compressedCount = 0 }, JsonOptions);

        var toolCalls = await repository.GetToolCallsForConversationAsync(conversationId.Value, ct);
        var toolCallsToCompress = conversation.CompressedAt == null
            ? toolCalls.ToList()
            : toolCalls.Where(t => t.CreatedAt <= conversation.CompressedAt.Value).ToList();

        var summary = await compressionService.GenerateSummaryAsync(
            messagesToCompress,
            toolCallsToCompress,
            agentConfig.GetToolResultMaxLength(),
            ct);

        if (string.IsNullOrWhiteSpace(summary))
            return JsonSerializer.Serialize(new { success = false, message = "Failed to generate summary", compressedCount = 0 }, JsonOptions);

        await repository.UpdateCompressionAsync(conversationId.Value, summary, DateTime.UtcNow, ct);

        return JsonSerializer.Serialize(new { success = true, message = $"Compressed {messagesToCompress.Count} messages. Context saved.", compressedCount = messagesToCompress.Count }, JsonOptions);
    }
}
