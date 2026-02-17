using System.Text;
using SmallEBot.Services.Terminal;

namespace SmallEBot.Services.Sandbox;

/// <summary>Executes Python via the python.exe in the run directory using ICommandRunner (same execution path as ExecuteCommand). Supports inline code or a script file path under the run directory.</summary>
public sealed class ProcessPythonSandbox(ICommandRunner commandRunner, ITerminalConfigService terminalConfig) : IPythonSandbox
{
    private static readonly IReadOnlyDictionary<string, string> PythonUtf8Env = new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" };

    /// <inheritdoc />
    public string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(scriptPath))
            return "Error: provide either code or scriptPath.";

        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var pythonExe = Path.Combine(baseDir, "python.exe");
        if (!File.Exists(pythonExe))
            return "Error: python.exe not found in the run directory. Place python.exe in the application run directory to use RunPython.";

        string scriptToRun;
        string workDir = baseDir;

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            var fullScriptPath = Path.GetFullPath(Path.Combine(baseDir, scriptPath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!fullScriptPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: path must be under the run directory.";
            if (!string.Equals(Path.GetExtension(fullScriptPath), ".py", StringComparison.OrdinalIgnoreCase))
                return "Error: script file not found or not a .py file.";
            if (!File.Exists(fullScriptPath))
                return "Error: script file not found or not a .py file.";
            scriptToRun = fullScriptPath;
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var fullWorkDir = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
                if (!fullWorkDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    return "Error: working directory must be under the run directory.";
                if (Directory.Exists(fullWorkDir))
                    workDir = fullWorkDir;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var fullWorkDir = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
                if (!fullWorkDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    return "Error: working directory must be under the run directory.";
                if (Directory.Exists(fullWorkDir))
                    workDir = fullWorkDir;
            }
            var tmpDir = Path.Combine(baseDir, ".agents", "tmp");
            try
            {
                if (!Directory.Exists(tmpDir))
                    Directory.CreateDirectory(tmpDir);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
            var tempPy = Path.Combine(tmpDir, $"run_{Guid.NewGuid():N}.py");
            try
            {
                File.WriteAllText(tempPy, code!.Trim(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
            try
            {
                var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(terminalConfig.GetCommandTimeoutSeconds());
                var command = Quoted(pythonExe) + " " + Quoted(tempPy);
                return commandRunner.Run(command, workDir, effectiveTimeout, PythonUtf8Env);
            }
            finally
            {
                try { File.Delete(tempPy); } catch { /* ignore */ }
            }
        }

        var timeoutEffective = timeout ?? TimeSpan.FromSeconds(terminalConfig.GetCommandTimeoutSeconds());
        var cmd = Quoted(pythonExe) + " " + Quoted(scriptToRun);
        return commandRunner.Run(cmd, workDir, timeoutEffective, PythonUtf8Env);
    }

    private static string Quoted(string path) => "\"" + path.Replace("\"", "\"\"") + "\"";
}
