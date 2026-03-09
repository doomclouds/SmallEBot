using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Agents.Context;

namespace SmallEBot.Application.Agents.Context;

/// <summary>AIContextProvider that injects turn-specific context as a user message via AIContext.Messages so the model attends to it.</summary>
public sealed class TurnContextProvider(IServiceProvider serviceProvider) : AIContextProvider
{
    private static readonly AsyncLocal<AgentTurnContext?> CurrentContext = new();

    public static void SetContext(AgentTurnContext? context) => CurrentContext.Value = context;
    public static void ClearContext() => CurrentContext.Value = null;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var turnContext = CurrentContext.Value;
        if (turnContext == null || turnContext.IsEmpty)
            return new AIContext();

        using var scope = serviceProvider.CreateScope();
        var fragmentBuilder = scope.ServiceProvider.GetRequiredService<ITurnContextFragmentBuilder>();
        var content = await fragmentBuilder.BuildContextHintAsync(
            turnContext.AttachedPaths,
            turnContext.RequestedSkillIds,
            cancellationToken);

        return string.IsNullOrWhiteSpace(content)
            ? new AIContext()
            : new AIContext { Messages = [new ChatMessage(ChatRole.User, content)] };
    }
}

/// <summary>Holds turn-specific context for AIContextProvider.</summary>
public sealed class AgentTurnContext
{
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
    public bool IsEmpty => AttachedPaths.Count == 0 && RequestedSkillIds.Count == 0;
}
