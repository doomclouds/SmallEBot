namespace SmallEBot.Services.Sandbox;

/// <summary>Executes Python code using python.exe in the run directory. Supports inline code or a script file path under the run directory.</summary>
public interface IPythonSandbox
{
    /// <summary>Executes Python code. Either code or scriptPath must be non-empty; if both provided, scriptPath is used. Paths must be under the run directory. Returns combined stdout and stderr, or an error message.</summary>
    string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout);
}
