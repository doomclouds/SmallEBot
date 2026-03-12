using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

public sealed class AmbientTaskListScope : IAmbientTaskListScope
{
    private static readonly AsyncLocal<Guid?> CurrentSubAgentId = new();

    public Guid? GetSubAgentId() => CurrentSubAgentId.Value;

    public IDisposable BeginScope(Guid subAgentId)
    {
        CurrentSubAgentId.Value = subAgentId;
        return new Scope(() => CurrentSubAgentId.Value = null);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
