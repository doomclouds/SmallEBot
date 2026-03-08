using SmallEBot.Application.Contracts.Agents.Execution;

namespace SmallEBot.Application.Agents.Execution;

/// <summary>Invalidates the cached agent when config changes. Scoped; disposes (invalidates) when circuit ends.</summary>
public sealed class AgentInvalidationService(IAgentBuilder agentBuilder) : IAgentInvalidationService, IAsyncDisposable
{
    public async Task InvalidateAgentAsync() => await agentBuilder.InvalidateAsync();

    public async ValueTask DisposeAsync()
    {
        await agentBuilder.InvalidateAsync();
    }
}
