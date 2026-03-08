using System.ComponentModel;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Agents.Tools;

namespace SmallEBot.Infrastructure.Agents.Tools;

/// <summary>Provides time-related tools.</summary>
public sealed class TimeToolProvider : IToolProvider
{
    public string Name => "Time";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GetCurrentTime);
    }

    [Description("Returns the current local date and time on the host machine.")]
    private static string GetCurrentTime() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss (ddd)");
}
