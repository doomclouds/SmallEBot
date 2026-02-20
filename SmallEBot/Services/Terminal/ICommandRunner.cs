namespace SmallEBot.Services.Terminal;

/// <summary>Runs a shell command on the host (same logic as ExecuteCommand). Used by the ExecuteCommand tool and by ProcessPythonSandbox.</summary>
public interface ICommandRunner
{
    /// <summary>Runs the command in the given working directory. Uses shell (cmd.exe on Windows, sh on Unix). Optional timeout; if null, uses ITerminalConfigService. Optional environment overrides (e.g. PYTHONIOENCODING) are applied to the process. Returns exit code, stdout and stderr, or an error message.</summary>
    string Run(string command, string workingDirectory, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environmentOverrides = null);

    /// <summary>Runs the command asynchronously with streaming output.</summary>
    IAsyncEnumerable<CommandOutput> RunStreamingAsync(
        string command,
        string workingDirectory,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        CancellationToken ct = default);
}

/// <summary>Output from command execution.</summary>
public record CommandOutput(OutputType Type, string Content);

/// <summary>Type of command output.</summary>
public enum OutputType { Stdout, Stderr, ExitCode }
