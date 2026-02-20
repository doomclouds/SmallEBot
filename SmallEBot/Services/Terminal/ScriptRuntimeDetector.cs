using System.Collections.Concurrent;
using System.Diagnostics;

namespace SmallEBot.Services.Terminal;

/// <summary>
/// Information about a detected script runtime (Python, PowerShell, Bash, etc.).
/// </summary>
/// <param name="Type">Runtime type: "python", "powershell", "bash", "cmd", "unknown", or "auto".</param>
/// <param name="Command">Executable command or path.</param>
/// <param name="Version">Version string from the runtime, if available.</param>
/// <param name="IsAvailable">Whether the runtime was successfully detected and is available for use.</param>
public record ScriptRuntimeInfo(
    string Type,
    string? Command,
    string? Version,
    bool IsAvailable
)
{
    /// <summary>
    /// Creates a result indicating the runtime was not found.
    /// </summary>
    public static ScriptRuntimeInfo NotFound(string type) =>
        new(type, null, null, false);

    /// <summary>
    /// Creates a result indicating automatic runtime selection should be used.
    /// </summary>
    public static ScriptRuntimeInfo Auto() =>
        new("auto", null, null, true);
}

/// <summary>
/// Detects available script runtimes (Python, PowerShell, Bash) on the system.
/// Results are cached for the lifetime of the detector.
/// </summary>
public class ScriptRuntimeDetector
{
    private readonly ConcurrentDictionary<string, ScriptRuntimeInfo> _cache = new();

    /// <summary>
    /// Detects the Python runtime. Tries "python", "python3", and "py".
    /// </summary>
    public ScriptRuntimeInfo DetectPython()
    {
        return _cache.GetOrAdd("python", _ =>
        {
            var candidates = new[] { "python", "python3", "py" };
            foreach (var cmd in candidates)
            {
                var result = TryGetVersion(cmd, "--version");
                if (result != null)
                {
                    return new ScriptRuntimeInfo("python", cmd, result, true);
                }
            }
            return ScriptRuntimeInfo.NotFound("python");
        });
    }

    /// <summary>
    /// Detects the PowerShell runtime. On Windows tries "pwsh" then "powershell"; on other platforms tries "pwsh".
    /// </summary>
    public ScriptRuntimeInfo DetectPowerShell()
    {
        return _cache.GetOrAdd("powershell", _ =>
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[] { "pwsh", "powershell" }
                : new[] { "pwsh" };

            foreach (var cmd in candidates)
            {
                var versionArg = OperatingSystem.IsWindows()
                    ? "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\""
                    : "--version";
                var result = TryGetVersion(cmd, versionArg);
                if (result != null)
                {
                    return new ScriptRuntimeInfo("powershell", cmd, result, true);
                }
            }
            return ScriptRuntimeInfo.NotFound("powershell");
        });
    }

    /// <summary>
    /// Detects the Bash runtime. On Windows checks Git Bash paths; on other platforms uses "bash".
    /// </summary>
    public ScriptRuntimeInfo DetectBash()
    {
        return _cache.GetOrAdd("bash", _ =>
        {
            if (OperatingSystem.IsWindows())
            {
                var gitBashPaths = new[]
                {
                    @"C:\Program Files\Git\bin\bash.exe",
                    @"C:\Program Files (x86)\Git\bin\bash.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "git", "current", "bin", "bash.exe")
                };

                foreach (var path in gitBashPaths)
                {
                    if (File.Exists(path))
                    {
                        var result = TryGetVersion(path, "--version");
                        if (result != null)
                        {
                            return new ScriptRuntimeInfo("bash", path, result, true);
                        }
                    }
                }

                return ScriptRuntimeInfo.NotFound("bash");
            }

            var bashResult = TryGetVersion("bash", "--version");
            if (bashResult != null)
            {
                return new ScriptRuntimeInfo("bash", "bash", bashResult, true);
            }

            return ScriptRuntimeInfo.NotFound("bash");
        });
    }

    /// <summary>
    /// Runs the command with the given version argument and returns the output if exit code is 0.
    /// Reads both stdout and stderr (some runtimes, e.g. Python, output version to stderr).
    /// </summary>
    private static string? TryGetVersion(string command, string versionArg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = versionArg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0) return null;

            var combined = (stdout + stderr).Trim();
            return combined.Length > 0 ? combined : null;
        }
        catch
        {
            return null;
        }
    }
}
