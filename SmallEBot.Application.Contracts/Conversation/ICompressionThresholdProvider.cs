namespace SmallEBot.Application.Conversation;

/// <summary>Provides compression threshold for automatic context compression.</summary>
public interface ICompressionThresholdProvider
{
    /// <summary>Context usage ratio threshold (0.0-1.0) that triggers automatic compression. Default: 0.8 (80%).</summary>
    double GetCompressionThreshold();
}
