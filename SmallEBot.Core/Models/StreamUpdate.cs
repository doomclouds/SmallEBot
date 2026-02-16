namespace SmallEBot.Core.Models;

public abstract record StreamUpdate;

public sealed record TextStreamUpdate(string Text) : StreamUpdate;

public sealed record ToolCallStreamUpdate(string ToolName, string? Arguments = null, string? Result = null) : StreamUpdate;

public sealed record ThinkStreamUpdate(string Text) : StreamUpdate;
