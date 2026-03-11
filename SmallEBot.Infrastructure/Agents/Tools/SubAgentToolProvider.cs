using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Application.Agents.SubAgents;
using SmallEBot.Application.Contracts.Agents.Tools;
using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Agents.Tools;

/// <summary>Provides sub-agent delegation tools (RunSubAgent, StopSubAgent).</summary>
/// <remarks>Uses IServiceScopeFactory to resolve Scoped SubAgentOrchestrator from a scope (breaks circular dependency and root-provider restriction).</remarks>
public sealed class SubAgentToolProvider(
    IServiceScopeFactory scopeFactory,
    IAmbientConversationId ambientConversationId) : IToolProvider
{
    public string Name => "SubAgent";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(RunSubAgent);
        yield return AIFunctionFactory.Create(StopSubAgent);
    }

    [Description("Run a sub-agent to perform a self-contained task (exploration, research, analysis). Pass identity (optional role/responsibilities) and task (required description). When identity is omitted, a default explorer sub-agent is used. Max 1 concurrent; a second call waits. Returns the aggregated text result from the sub-agent.")]
    private async Task<string> RunSubAgent(string? identity, string task)
    {
        if (string.IsNullOrWhiteSpace(task))
            return "Error: task is required.";
        if (ambientConversationId.GetConversationId() == null)
            return "Error: Sub-agent must run within a conversation scope.";
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<SubAgentOrchestrator>();
            return await orchestrator.RunAsync(identity, task);
        }
        catch (InvalidOperationException ex)
        {
            return "Error: " + ex.Message;
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Stop a running sub-agent by its id (Guid format). Call when you need to abort a sub-agent. Returns \"Stopped\" on success or an error message if the id is invalid.")]
    private async Task<string> StopSubAgent(string subAgentId)
    {
        if (string.IsNullOrWhiteSpace(subAgentId))
            return "Error: subAgentId is required.";
        if (!Guid.TryParse(subAgentId, out var id))
            return "Error: subAgentId must be a valid Guid format.";
        using var scope = scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<SubAgentOrchestrator>();
        await orchestrator.StopAsync(id);
        return "Stopped";
    }
}
