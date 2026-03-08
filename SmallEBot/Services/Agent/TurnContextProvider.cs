using Microsoft.Agents.AI;
using SmallEBot.Application.Contracts.Conversations;

namespace SmallEBot.Services.Agent;

/// <summary>AIContextProvider that injects turn-specific context via ITurnContextFragmentBuilder. Uses AsyncLocal to pass context across async boundaries.</summary>
public sealed class TurnContextProvider(IServiceProvider serviceProvider) : AIContextProvider
{
    private static readonly AsyncLocal<TurnContext?> CurrentContext = new();

    public static void SetContext(TurnContext? context) => CurrentContext.Value = context;
    public static void ClearContext() => CurrentContext.Value = null;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var turnContext = CurrentContext.Value;
        if (turnContext == null || turnContext.IsEmpty)
            return new AIContext();

        using var scope = serviceProvider.CreateScope();
        var fragmentBuilder = scope.ServiceProvider.GetRequiredService<ITurnContextFragmentBuilder>();
        var instructions = await fragmentBuilder.BuildContextHintAsync(
            turnContext.AttachedPaths,
            turnContext.RequestedSkillIds,
            cancellationToken);

        return string.IsNullOrWhiteSpace(instructions)
            ? new AIContext()
            : new AIContext { Instructions = instructions };
    }
}

/// <summary>Holds turn-specific context for AIContextProvider.</summary>
public sealed class TurnContext
{
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
    public bool IsEmpty => AttachedPaths.Count == 0 && RequestedSkillIds.Count == 0;
}
