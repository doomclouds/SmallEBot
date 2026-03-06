using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent;

/// <summary>
/// AIContextProvider that injects turn-specific context (attached files, requested skills).
/// Uses AsyncLocal to pass context across async boundaries.
/// </summary>
public sealed class TurnContextProvider : AIContextProvider
{
    private static readonly AsyncLocal<TurnContext?> _currentContext = new();

    /// <summary>
    /// Set the current turn context (call before running agent).
    /// </summary>
    public static void SetContext(TurnContext? context) => _currentContext.Value = context;

    /// <summary>
    /// Get the current turn context.
    /// </summary>
    public static TurnContext? GetContext() => _currentContext.Value;

    /// <summary>
    /// Clear the current turn context (call after running agent).
    /// </summary>
    public static void ClearContext() => _currentContext.Value = null;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var turnContext = _currentContext.Value;
        if (turnContext == null || turnContext.IsEmpty)
        {
            return new ValueTask<AIContext>(new AIContext());
        }

        var instructions = BuildInstructions(turnContext);
        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions
        });
    }

    private static string? BuildInstructions(TurnContext context)
    {
        var parts = new List<string>();

        if (context.AttachedPaths.Count > 0)
        {
            parts.Add("""
                # Attached Files

                The following files are attached to this message. Use ReadFile to read their contents when needed:
                """ + "\n" + string.Join("\n", context.AttachedPaths.Select(p => $"- {p}")));
        }

        if (context.RequestedSkillIds.Count > 0)
        {
            parts.Add("""
                # Requested Skills

                """ + string.Join("\n", context.RequestedSkillIds.Select(s => $"The user wants you to use the skill \"{s}\". Call load_skill(\"{s}\") to learn and apply it.")));
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }
}

/// <summary>
/// Holds turn-specific context for AIContextProvider.
/// </summary>
public sealed class TurnContext
{
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];

    public bool IsEmpty => AttachedPaths.Count == 0 && RequestedSkillIds.Count == 0;
}
