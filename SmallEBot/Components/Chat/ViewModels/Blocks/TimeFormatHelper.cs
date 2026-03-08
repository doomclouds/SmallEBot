namespace SmallEBot.Components.Chat.ViewModels.Blocks;

/// <summary>Shared elapsed time formatting for ToolCallBlock and WaitingBlock.</summary>
public static class TimeFormatHelper
{
    public static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null) return "";
        var e = elapsed.Value;
        if (e.TotalMinutes >= 1) return $"{(int)e.TotalMinutes}m {e.Seconds}s";
        if (e.TotalSeconds >= 1) return $"{e.TotalSeconds:F1}s";
        return $"{e.TotalMilliseconds:F0}ms";
    }
}
