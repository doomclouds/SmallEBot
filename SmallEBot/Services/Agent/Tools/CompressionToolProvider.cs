using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides context compression capability. Not exposed to LLM - used internally by AgentRunnerAdapter for auto-compression.</summary>
public sealed class CompressionToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository,
    IAgentConfigService agentConfig,
    ICompressionService compressionService) : IToolProvider
{
    public string Name => "Compression";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        // This tool is NOT exposed to the LLM - it's called internally
        // Return empty to hide from LLM
        yield break;
    }

    /// <summary>Compress conversation history and store summary. Called by AgentRunnerAdapter when context exceeds threshold.</summary>
    public async Task<CompressionResult> CompactContextAsync(CancellationToken ct = default)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return new CompressionResult(false, "No active conversation");

        // Get conversation to check existing compression
        var conversation = await repository.GetByIdAsync(conversationId.Value, "", ct);
        if (conversation == null)
            return new CompressionResult(false, "Conversation not found");

        // Get messages to compress (those before existing CompressedAt, or all if first compression)
        var allMessages = await repository.GetMessagesForConversationAsync(conversationId.Value, ct);
        var messagesToCompress = conversation.CompressedAt == null
            ? allMessages
            : allMessages.Where(m => m.CreatedAt <= conversation.CompressedAt.Value).ToList();

        if (messagesToCompress.Count == 0)
            return new CompressionResult(false, "No messages to compress");

        // Get tool calls for context
        var toolCalls = await repository.GetToolCallsForConversationAsync(conversationId.Value, ct);
        var toolCallsToCompress = conversation.CompressedAt == null
            ? toolCalls
            : toolCalls.Where(t => t.CreatedAt <= conversation.CompressedAt.Value).ToList();

        // Call LLM to generate summary
        var summary = await compressionService.GenerateSummaryAsync(
            messagesToCompress,
            toolCallsToCompress,
            agentConfig.GetToolResultMaxLength(),
            ct);

        if (string.IsNullOrWhiteSpace(summary))
            return new CompressionResult(false, "Failed to generate summary");

        // Store compressed context
        var newCompressedAt = DateTime.UtcNow;
        await repository.UpdateCompressionAsync(conversationId.Value, summary, newCompressedAt, ct);

        return new CompressionResult(true, $"Compressed {messagesToCompress.Count} messages", summary);
    }
}

/// <summary>Result of context compression operation.</summary>
public record CompressionResult(bool Success, string Message, string? Summary = null);
