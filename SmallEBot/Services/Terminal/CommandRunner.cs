using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmallEBot.Services.Terminal;

/// <summary>Runs a shell command via cmd.exe (Windows) or sh (Unix). Shared by ExecuteCommand tool and ProcessPythonSandbox.</summary>
public sealed class CommandRunner(ITerminalConfigService terminalConfig) : ICommandRunner
{
    /// <inheritdoc />
    public string Run(string command, string workingDirectory, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        var normalized = command.Trim();
        var timeoutMs = timeout.HasValue
            ? (int)Math.Clamp(timeout.Value.TotalMilliseconds, 100, int.MaxValue)
            : Math.Clamp(terminalConfig.GetCommandTimeoutSeconds(), 5, 600) * 1000;

        try
        {
            using var process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WorkingDirectory = workingDirectory;

            if (environmentOverrides != null)
            {
                foreach (var (key, value) in environmentOverrides)
                    process.StartInfo.Environment[key] = value;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c \"{normalized.Replace("\"", "\"\"")}\"";
            }
            else
            {
                process.StartInfo.FileName = "/bin/sh";
                process.StartInfo.Arguments = $"-c \"{normalized.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(timeoutMs);
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (!exited)
            {
                try { process.Kill(); } catch { /* ignore */ }
                return $"Error: Command timed out after {timeoutMs / 1000} seconds.";
            }

            return $"ExitCode: {process.ExitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}";
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }
}
