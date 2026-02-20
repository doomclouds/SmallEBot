using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Terminal;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides shell command execution tool.</summary>
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandConfirmationService confirmationService,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
{
    public string Name => "Shell";
    public bool IsEnabled => true;

    public TimeSpan? GetTimeout(string toolName) => toolName switch
    {
        "ExecuteCommand" => TimeSpan.FromMinutes(10),
        _ => null
    };

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ExecuteCommand);
    }

    [Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the workspace root and defaults to the workspace root. Blocks until the command exits or the configured timeout (see Terminal config). Not allowed if the command matches the terminal blacklist. When confirmation is enabled, you must wait for user approval.")]
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

        if (await terminalConfig.GetRequireCommandConfirmationAsync(cancellationToken))
        {
            var whitelist = await terminalConfig.GetCommandWhitelistAsync(cancellationToken);
            var allowedByWhitelist = whitelist.Any(w =>
                normalized.Equals(w, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            if (!allowedByWhitelist)
            {
                var timeoutSeconds = await terminalConfig.GetConfirmationTimeoutSecondsAsync(cancellationToken);
                var result = await confirmationService.RequestConfirmationAsync(normalized, workDir, timeoutSeconds, cancellationToken);
                if (result != CommandConfirmResult.Allow)
                    return "Error: Command was not approved (rejected or timed out).";
                _ = terminalConfig.AddToWhitelistAndSaveAsync(normalized, cancellationToken);
            }
        }

        var timeout = GetTimeout("ExecuteCommand");
        var output = commandRunner.Run(normalized, workDir, timeout);
        const int maxOutputChars = 50_000;
        if (output.Length > maxOutputChars)
            output = output[..maxOutputChars] + $"\n\n[Output truncated: {output.Length} total chars, showing first {maxOutputChars}]";
        return output;
    }
}
