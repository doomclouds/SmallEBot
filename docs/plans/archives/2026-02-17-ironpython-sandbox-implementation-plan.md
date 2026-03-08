# IronPython Sandbox and RunPython Tool — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an in-process Python executor using IronPython and a built-in agent tool RunPython that accepts inline code and/or a script path; no dependency on system Python.

**Architecture:** Host-only: interface `IPythonSandbox` and implementation `IronPythonSandbox` in SmallEBot; single shared engine, scope per execution, stdout/stderr captured via `Runtime.IO`. Tool `RunPython(code?, scriptPath?, workingDirectory?)` in `BuiltInToolFactory` validates inputs and delegates to the sandbox. Paths under run directory; if both code and scriptPath are provided, scriptPath wins.

**Tech Stack:** .NET 10, IronPython (NuGet), existing `ITerminalConfigService` for timeout.

**Reference design:** `docs/plans/2026-02-17-ironpython-sandbox-design.md`

---

## Task 1: Add IronPython package and create IPythonSandbox

**Files:**
- Modify: `SmallEBot/SmallEBot.csproj`
- Create: `SmallEBot/Services/Sandbox/IPythonSandbox.cs`

**Step 1: Add NuGet package**

In `SmallEBot/SmallEBot.csproj`, inside the existing `<ItemGroup>` that has `PackageReference` entries, add:

```xml
<PackageReference Include="IronPython" Version="3.4.2" />
```

**Step 2: Build to verify package restore**

Run (from repo root):

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeds.

**Step 3: Create interface**

Create file `SmallEBot/Services/Sandbox/IPythonSandbox.cs`:

```csharp
namespace SmallEBot.Services.Sandbox;

/// <summary>Executes Python code in-process via IronPython. Supports inline code or a script file path under the run directory.</summary>
public interface IPythonSandbox
{
    /// <summary>Executes Python code. Either code or scriptPath must be non-empty; if both provided, scriptPath is used. Paths must be under the run directory. Returns combined stdout and stderr, or an error message.</summary>
    string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout);
}
```

**Step 4: Build again**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/SmallEBot.csproj SmallEBot/Services/Sandbox/IPythonSandbox.cs
git commit -m "feat(sandbox): add IronPython package and IPythonSandbox interface"
```

---

## Task 2: Implement IronPythonSandbox (engine, IO redirect, timeout)

**Files:**
- Create: `SmallEBot/Services/Sandbox/IronPythonSandbox.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Implement IronPythonSandbox**

Create `SmallEBot/Services/Sandbox/IronPythonSandbox.cs` with the following behavior:

- One `ScriptEngine` created in the constructor and held for the lifetime of the instance (lazy or in ctor; design allows either). Use `IronPython.Hosting.Python.CreateEngine()`.
- `Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout)`:
  - If both `code` and `scriptPath` are null/whitespace, return `"Error: provide either code or scriptPath."`
  - Resolve `scriptPath` and `workingDirectory` against `Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)`. If either resolved path is not under base (string compare ordinal ignore case), return `"Error: path must be under the run directory."`
  - If `scriptPath` is provided: ensure extension is `.py`, file exists; else return `"Error: script file not found or not a .py file."` Then read file content and use as `code` for execution (overwriting any inline `code` — scriptPath wins).
  - If only `code` is provided, use it as-is (trimmed).
  - Timeout: use `timeout ?? TimeSpan.FromSeconds(60)` (or inject `ITerminalConfigService` and use `GetCommandTimeoutSeconds()`; design says “optional” — for this plan use 60s default if no timeout passed).
  - Create two `StringWriter` instances (UTF-8) for stdout and stderr. Set `engine.Runtime.IO.SetOutput(stdoutStream, Encoding.UTF8)` and `SetErrorStream(stderrStream, Encoding.UTF8)` (IronPython 3 API: check for `SetOutput`/`OutputStream` — use the API that accepts a TextWriter or Stream; if only Stream, use a `MemoryStream` and `StreamWriter`). Before execute, create a new scope with `engine.CreateScope()`.
  - Run `engine.Execute(code, scope)` inside `Task.Run(() => { ... })`. Wait with `task.WaitAsync(timeout.Value)`. If timeout, return `"Error: Script timed out after N seconds."` (N = timeout.TotalSeconds). On success, return `"Stdout:\n" + stdoutContent + "\nStderr:\n" + stderrContent`.
  - Catch exceptions from `Execute`; return `"Error: " + ex.Message`. If the engine exposes a traceback (e.g. via exception or `engine.GetService<ExceptionOperations>()`), append it to the message for debugging.

Reference IronPython 3 API for IO: `runtime.IO.SetOutput(Stream, Encoding)` and `SetErrorStream(Stream, Encoding)` — if the API takes `TextWriter`, use `StringWriter`; if it takes `Stream`, wrap `MemoryStream` with `StreamWriter` and flush after execute, then read string from the stream or use a backing `StringWriter` passed to a `StreamWriter` subclass. Exact API: see IronPython 3.4 documentation or source for `Microsoft.Scripting.Hosting` — typically `Runtime.IO.SetOutput(Stream, Encoding)` and `SetErrorStream(Stream, Encoding)`.

**Step 2: Register in DI**

In `SmallEBot/Extensions/ServiceCollectionExtensions.cs`:
- Add: `using SmallEBot.Services.Sandbox;`
- After the line that registers `ITerminalConfigService`, add: `services.AddSingleton<IPythonSandbox, IronPythonSandbox>();`

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Resolve any API names (e.g. `SetOutput` vs `SetOutputToStream`) from IronPython package.

**Step 4: Commit**

```bash
git add SmallEBot/Services/Sandbox/IronPythonSandbox.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(sandbox): implement IronPythonSandbox with IO redirect and timeout"
```

---

## Task 3: Add RunPython to BuiltInToolFactory

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Inject IPythonSandbox and add RunPython tool**

- Add constructor parameter and field for `IPythonSandbox` (e.g. `pythonSandbox`). So the primary constructor becomes: `BuiltInToolFactory(ITerminalConfigService terminalConfig, IPythonSandbox pythonSandbox)`.
- Add `using SmallEBot.Services.Sandbox;` at the top.
- In `CreateTools()`, add a fourth tool: `AIFunctionFactory.Create(RunPython)` (so the array has GetCurrentTime, ReadFile, ReadSkill, ExecuteCommand, RunPython).
- Add instance method:

```csharp
[Description("Run Python code in a sandbox (IronPython). Provide either code (inline Python) or scriptPath (path to a .py file under the run directory, e.g. .agents/sys.skills/my-skill/script.py). If both are provided, scriptPath is used. Optional workingDirectory is relative to the run directory. Output is stdout and stderr; execution has a timeout (see Terminal config).")]
private string RunPython(string? code = null, string? scriptPath = null, string? workingDirectory = null)
{
    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(scriptPath))
        return "Error: provide either code or scriptPath.";
    var timeoutSec = Math.Clamp(terminalConfig.GetCommandTimeoutSeconds(), 5, 600);
    var timeout = TimeSpan.FromSeconds(timeoutSec);
    return pythonSandbox.Execute(code?.Trim(), scriptPath?.Trim(), string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim(), timeout);
}
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(agent): add RunPython built-in tool"
```

---

## Task 4: Update CLAUDE.md and system prompt

**Files:**
- Modify: `CLAUDE.md`
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs` (optional: mention RunPython in BaseInstructions)

**Step 1: Update CLAUDE.md**

In the Architecture section where built-in tools are listed (e.g. “Built-in tools: **ReadSkill** … **ReadFile** …”), add a sentence: “**RunPython(code?, scriptPath?, workingDirectory?)** runs Python in an IronPython sandbox (no system Python required); pass inline code or a path to a `.py` file under the run directory; if both are provided, scriptPath is used.”

**Step 2: Optionally extend system prompt**

In `SmallEBot/Services/Agent/AgentContextFactory.cs`, in `BaseInstructions`, add a short phrase so the agent knows it can use RunPython for Python scripts, e.g. “For running Python scripts or snippets, use the RunPython tool (inline code or path to a .py file under the run directory).”

**Step 3: Commit**

```bash
git add CLAUDE.md SmallEBot/Services/Agent/AgentContextFactory.cs
git commit -m "docs: document RunPython in CLAUDE.md and system prompt"
```

---

## Task 5: Manual verification

**Files:** None (manual test).

**Step 1: Run the app**

From repo root: `dotnet run --project SmallEBot`

**Step 2: Invoke RunPython via chat**

In the Blazor UI, start a conversation and ask the agent to run a simple Python snippet, e.g. “Use RunPython to execute print(‘hello py’) and tell me the output.” Then try “Use RunPython with scriptPath .agents/sys.skills/weekly-report-generator/scripts/parse_weekly_report.py” or another existing .py under run directory (expect possible script-specific errors if the script needs args or env, but the tool should run and return Stdout/Stderr or an error).

**Step 3: Confirm**

- Inline code returns Stdout:\n...\nStderr:\n...
- scriptPath (valid .py under base) causes the file to be read and executed; invalid path returns the design error messages.

---

## Summary checklist

- [ ] Task 1: IronPython package + IPythonSandbox interface
- [ ] Task 2: IronPythonSandbox implementation + DI registration
- [ ] Task 3: RunPython in BuiltInToolFactory
- [ ] Task 4: CLAUDE.md and optional system prompt
- [ ] Task 5: Manual run and chat verification
