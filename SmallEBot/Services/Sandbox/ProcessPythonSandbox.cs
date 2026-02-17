using System.Diagnostics;
using System.Text;

namespace SmallEBot.Services.Sandbox;

/// <summary>Executes Python via the python.exe in the run directory. Supports inline code or a script file path under the run directory.</summary>
public sealed class ProcessPythonSandbox : IPythonSandbox
{
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
                var result = RunProcess(pythonExe, tempPy, workDir, timeout ?? TimeSpan.FromSeconds(60));
                return result;
            }
            finally
            {
                try { File.Delete(tempPy); } catch { /* ignore */ }
            }
        }

        return RunProcess(pythonExe, scriptToRun, workDir, timeout ?? TimeSpan.FromSeconds(60));
    }

    private static string RunProcess(string pythonExe, string scriptPath, string workingDirectory, TimeSpan timeout)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = pythonExe;
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            process.Start();
            var timeoutMs = (int)Math.Clamp(timeout.TotalMilliseconds, 100, int.MaxValue);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(timeoutMs);
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (!exited)
            {
                try { process.Kill(); } catch { /* ignore */ }
                return $"Error: Script timed out after {timeout.TotalSeconds:F0} seconds.";
            }
            return "Stdout:\n" + stdout + "\nStderr:\n" + stderr;
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }
}
