using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Repositories;
using SmallEBot.Services.Agent.Tools.Conversation;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides conversation data reading tools.</summary>
public sealed class ConversationToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository,
    IAgentConfigService agentConfig) : IToolProvider
{

    public string Name => "Conversation";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ReadConversationData);
    }

    [Description("Read the complete execution history of the current conversation including user messages, assistant messages, thinking blocks, and tool calls. Returns a JSON object with 'events' array sorted by timestamp and 'summary' with statistics. Use this to analyze execution patterns when the user wants to create or improve skills based on conversation history.")]
    private async Task<string> ReadConversationData()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return JsonSerializer.Serialize(new { ok = false, error = "No active conversation context." });

        // Load all data in parallel
        var messages = await repository.GetMessagesForConversationAsync(conversationId.Value);
        var toolCalls = await repository.GetToolCallsForConversationAsync(conversationId.Value);
        var thinkBlocks = await repository.GetThinkBlocksForConversationAsync(conversationId.Value);

        // Build events list
        var events = new List<ConversationEventView>();

        // Add messages
        foreach (var msg in messages.Where(m => m.ReplacedByMessageId == null))
        {
            events.Add(new ConversationEventView
            {
                Type = msg.Role == "user" ? "user_message" : "assistant_message",
                Content = msg.Content,
                Timestamp = msg.CreatedAt.ToString("O"),
                Role = msg.Role,
                AttachedPaths = msg.Role == "user" ? msg.AttachedPaths : null,
                RequestedSkillIds = msg.Role == "user" ? msg.RequestedSkillIds : null
            });
        }

        // Add think blocks
        foreach (var think in thinkBlocks)
        {
            events.Add(new ConversationEventView
            {
                Type = "think",
                Content = think.Content,
                Timestamp = think.CreatedAt.ToString("O")
            });
        }

        // Add tool calls
        foreach (var tc in toolCalls)
        {
            events.Add(new ConversationEventView
            {
                Type = "tool_call",
                Timestamp = tc.CreatedAt.ToString("O"),
                ToolName = tc.ToolName,
                Arguments = tc.Arguments,
                Result = TruncateResult(tc.Result)
            });
        }

        // Sort by timestamp
        var sortedEvents = events.OrderBy(e => e.Timestamp).ToList();

        // Build summary
        var toolUsage = toolCalls
            .GroupBy(tc => tc.ToolName)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new ConversationDataView
        {
            ConversationId = conversationId.Value.ToString(),
            Events = sortedEvents,
            Summary = new ConversationSummaryView
            {
                TotalEvents = sortedEvents.Count,
                ToolCallCount = toolCalls.Count,
                ToolUsage = toolUsage
            }
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private string? TruncateResult(string? result)
    {
        if (result == null) return null;
        var maxLength = agentConfig.GetToolResultMaxLength();
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }
}
