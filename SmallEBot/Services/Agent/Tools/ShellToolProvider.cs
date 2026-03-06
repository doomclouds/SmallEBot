using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Terminal;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides shell command execution tool.</summary>
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
{
    /// <inheritdoc />
    public string Name => "Shell";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
#pragma warning disable MEAI001 // Type is for evaluation purposes only
    public IEnumerable<AITool> GetTools()
    {
        var tool = AIFunctionFactory.Create(ExecuteCommand);
        var requiresConfirmation = terminalConfig.GetRequireCommandConfirmation();

        if (requiresConfirmation)
        {
            yield return new ApprovalRequiredAIFunction(tool);
        }
        else
        {
            yield return tool;
        }
    }
#pragma warning restore MEAI001

    /// <inheritdoc />
    public TimeSpan? GetTimeout(string toolName) => null;

    [Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the workspace root and defaults to the workspace root. Blocks until the command exits or the configured timeout (see Terminal config). Not allowed if the command matches the terminal blacklist.")]
    private async Task<string> ExecuteCommand(string command, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";
        var normalized = Regex.Replace(command.Trim(), @"\s+", " ");
        var blacklist = await terminalConfig.GetCommandBlacklistAsync(cancellationToken);
        if (blacklist.Any(b => normalized.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Error: Command is not allowed by terminal blacklist.";

        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var workDir = baseDir;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: Working directory must be under the workspace.";
            if (!Directory.Exists(combined))
                return "Error: Working directory does not exist.";
            workDir = combined;
        }

        var timeout = GetTimeout("ExecuteCommand");
        var output = commandRunner.Run(normalized, workDir, timeout);
        const int maxOutputChars = 50_000;
        if (output.Length > maxOutputChars)
            output = output[..maxOutputChars] + $"\n\n[Output truncated: {output.Length} total chars, showing first {maxOutputChars}]";
        return output;
    }
}
