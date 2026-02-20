namespace SmallEBot.Core.Models;

/// <summary>Structured result from tool execution.</summary>
public record ToolResult(
    bool Success,
    string? Output,
    ToolError? Error,
    TimeSpan Elapsed)
{
    public static ToolResult Ok(string output, TimeSpan elapsed) =>
        new(true, output, null, elapsed);

    public static ToolResult Fail(string code, string message, bool retryable, TimeSpan elapsed) =>
        new(false, null, new ToolError(code, message, retryable), elapsed);
}

/// <summary>Structured error information.</summary>
public record ToolError(
    string Code,
    string Message,
    bool Retryable);
