using System.Text;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace SmallEBot.Services.Sandbox;

/// <summary>Executes Python code in-process via IronPython. Single shared engine; scope per execution; stdout/stderr captured.</summary>
public sealed class IronPythonSandbox : IPythonSandbox
{
    private readonly ScriptEngine _engine;

    public IronPythonSandbox()
    {
        _engine = Python.CreateEngine();
    }

    /// <inheritdoc />
    public string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(scriptPath))
            return "Error: provide either code or scriptPath.";

        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            var fullScriptPath = Path.GetFullPath(Path.Combine(baseDir, scriptPath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!fullScriptPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: path must be under the run directory.";
            if (!string.Equals(Path.GetExtension(fullScriptPath), ".py", StringComparison.OrdinalIgnoreCase))
                return "Error: script file not found or not a .py file.";
            if (!File.Exists(fullScriptPath))
                return "Error: script file not found or not a .py file.";
            try
            {
                code = File.ReadAllText(fullScriptPath);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
        else
        {
            code = code!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var fullWorkDir = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!fullWorkDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: path must be under the run directory.";
            // Working directory is validated; IronPython may not support changing cwd; we still accept it for future use.
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);

        var stdoutStream = new MemoryStream();
        var stderrStream = new MemoryStream();
        var stdoutWriter = new StreamWriter(stdoutStream, Encoding.UTF8);
        var stderrWriter = new StreamWriter(stderrStream, Encoding.UTF8);

        try
        {
            _engine.Runtime.IO.SetOutput(stdoutStream, stdoutWriter);
            _engine.Runtime.IO.SetErrorOutput(stderrStream, stderrWriter);
        }
        catch
        {
            // IronPython 3 API may differ; try alternative names.
            stdoutWriter.Dispose();
            stderrWriter.Dispose();
            return "Error: Failed to set up output streams (IronPython IO API).";
        }

        var scope = _engine.CreateScope();
        var task = Task.Run(() =>
        {
            try
            {
                _engine.Execute(code, scope);
            }
            finally
            {
                try
                {
                    stdoutWriter.Flush();
                    stderrWriter.Flush();
                }
                catch { /* ignore */ }
            }
        });

        try
        {
            if (!task.Wait(effectiveTimeout))
                return $"Error: Script timed out after {effectiveTimeout.TotalSeconds:F0} seconds.";
        }
        catch (AggregateException)
        {
            // Fall through to read stdout/stderr and then report the exception.
        }

        string ReadBack()
        {
            try
            {
                stdoutWriter.Flush();
                stderrWriter.Flush();
                stdoutStream.Position = 0;
                stderrStream.Position = 0;
                var stdout = new StreamReader(stdoutStream, Encoding.UTF8).ReadToEnd();
                var stderr = new StreamReader(stderrStream, Encoding.UTF8).ReadToEnd();
                return "Stdout:\n" + stdout + "\nStderr:\n" + stderr;
            }
            finally
            {
                stdoutWriter.Dispose();
                stderrWriter.Dispose();
                stdoutStream.Dispose();
                stderrStream.Dispose();
            }
        }

        if (task.IsFaulted && task.Exception != null)
        {
            var inner = task.Exception.InnerException ?? task.Exception;
            var output = ReadBack();
            return output + "\nError: " + inner.Message;
        }

        return ReadBack();
    }
}
