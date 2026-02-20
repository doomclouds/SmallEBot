namespace SmallEBot.Core.Models;

public abstract record StreamUpdate;

public sealed record TextStreamUpdate(string Text) : StreamUpdate;

public sealed record ThinkStreamUpdate(string Text) : StreamUpdate;

public enum ToolCallPhase
{
    Started,      // Tool call initiated, args streaming from LLM
    ArgsReceived, // All arguments received, about to execute
    Executing,    // Tool function is running
    Completed,    // Execution succeeded
    Failed,       // Execution failed
    Cancelled     // Cancelled by user
}

/// <summary>
/// Represents tool call progress events during streaming.
/// CallId links Started/ArgsReceived/Executing/Completed events for the same call.
/// </summary>
public sealed record ToolCallStreamUpdate(
    string ToolName,
    string? CallId = null,
    ToolCallPhase Phase = ToolCallPhase.Started,
    string? Arguments = null,
    string? Result = null,
    TimeSpan? Elapsed = null,
    string? Error = null) : StreamUpdate;
