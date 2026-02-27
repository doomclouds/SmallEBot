using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.Conversation;

/// <summary>Complete conversation data returned by ReadConversationData tool.</summary>
public sealed class ConversationDataView
{
    [JsonPropertyName("conversationId")]
    public required string ConversationId { get; init; }

    [JsonPropertyName("events")]
    public required IReadOnlyList<ConversationEventView> Events { get; init; }

    [JsonPropertyName("summary")]
    public required ConversationSummaryView Summary { get; init; }
}

public sealed class ConversationSummaryView
{
    [JsonPropertyName("totalEvents")]
    public required int TotalEvents { get; init; }

    [JsonPropertyName("toolCallCount")]
    public required int ToolCallCount { get; init; }

    [JsonPropertyName("toolUsage")]
    public required Dictionary<string, int> ToolUsage { get; init; }
}
