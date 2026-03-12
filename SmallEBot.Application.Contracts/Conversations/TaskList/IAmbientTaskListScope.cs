namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>Stores the current sub-agent id in AsyncLocal. Null = main agent scope.</summary>
public interface IAmbientTaskListScope
{
    Guid? GetSubAgentId();
    IDisposable BeginScope(Guid subAgentId);
}
