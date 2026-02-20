using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

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

    /// <inheritdoc />
    public async IAsyncEnumerable<CommandOutput> RunStreamingAsync(
        string command,
        string workingDirectory,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var normalized = command.Trim();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(Math.Clamp(terminalConfig.GetCommandTimeoutSeconds(), 5, 600));
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "cmd.exe" : "/bin/sh";
        var shellArg = isWindows ? "/c" : "-c";
        var args = isWindows ? $"{shellArg} \"{normalized.Replace("\"", "\"\"")}\"" : $"{shellArg} \"{normalized.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environmentOverrides != null)
        {
            foreach (var (key, value) in environmentOverrides)
                psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        process.Start();

        var stdoutStream = ReadStreamAsync(process.StandardOutput, OutputType.Stdout, cts.Token);
        var stderrStream = ReadStreamAsync(process.StandardError, OutputType.Stderr, cts.Token);

        await foreach (var output in MergeStreamsAsync(stdoutStream, stderrStream, cts.Token))
        {
            yield return output;
        }

        var exitCode = await WaitForExitOrKillAsync(process, cts.Token);
        yield return new CommandOutput(OutputType.ExitCode, exitCode.ToString());
    }

    private static async Task<int> WaitForExitOrKillAsync(Process process, CancellationToken ct)
    {
        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return -1;
        }
    }

    private static async IAsyncEnumerable<CommandOutput> ReadStreamAsync(
        StreamReader reader,
        OutputType type,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            yield return new CommandOutput(type, line);
        }
    }

    private static async IAsyncEnumerable<CommandOutput> MergeStreamsAsync(
        IAsyncEnumerable<CommandOutput> stream1,
        IAsyncEnumerable<CommandOutput> stream2,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<CommandOutput>();

        async Task ReadToChannel(IAsyncEnumerable<CommandOutput> source)
        {
            await foreach (var item in source.WithCancellation(ct))
                await channel.Writer.WriteAsync(item, ct);
        }

        var t1 = ReadToChannel(stream1);
        var t2 = ReadToChannel(stream2);
        _ = Task.WhenAll(t1, t2).ContinueWith(_ => channel.Writer.Complete(), ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }
}
