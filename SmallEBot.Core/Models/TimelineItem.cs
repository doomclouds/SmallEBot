namespace SmallEBot.Core.Models;

/// <summary>
/// One entry in the conversation timeline (message, tool call, or think block), sorted by CreatedAt.
/// Uses simple data types instead of entity references.
/// </summary>
public sealed record TimelineItem
{
    /// <summary>User or assistant message info (for text content).</summary>
    public MessageInfo? Message { get; init; }

    /// <summary>Tool call info (for function calls).</summary>
    public ToolCallInfo? ToolCall { get; init; }

    /// <summary>Think block info (for reasoning content).</summary>
    public ThinkBlockInfo? ThinkBlock { get; init; }

    public DateTime CreatedAt => Message?.CreatedAt ?? ToolCall?.CreatedAt ?? ThinkBlock!.CreatedAt;
}

/// <summary>Simplified message info for timeline display.</summary>
public sealed record MessageInfo
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }  // "user" or "assistant"
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsEdited { get; init; }
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
}

/// <summary>Simplified tool call info for timeline display.</summary>
public sealed record ToolCallInfo
{
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Simplified think block info for timeline display.</summary>
public sealed record ThinkBlockInfo
{
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
}
