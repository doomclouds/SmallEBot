# Terminal command execution and terminal config — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add the ExecuteCommand built-in tool (cross-platform shell execution with timeout and optional working directory), a command blacklist persisted in `.agents/terminal.json`, and a Terminal config dialog in the App bar to edit the blacklist. No streaming output.

**Architecture:** `ITerminalConfigService` (Singleton) reads/writes `.agents/terminal.json` and exposes sync `GetCommandBlacklist()` for the tool and async load/save for the UI. `BuiltInToolFactory` becomes stateful, injects `ITerminalConfigService`, and adds one ExecuteCommand tool that checks the blacklist then runs the process via a small helper. Windows uses `cmd.exe /c`; Linux/macOS use `/bin/sh -c`. Default blacklist is built-in; when file is missing, the default is used.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, existing SmallEBot Host/Application. No test project (verify by build and manual run).

**Reference design:** `docs/plans/2026-02-16-terminal-command-execution-design.md`

---

## Task 1: Add ITerminalConfigService and TerminalConfigService

**Files:**
- Create: `SmallEBot/Services/Terminal/ITerminalConfigService.cs`
- Create: `SmallEBot/Services/Terminal/TerminalConfigService.cs`

**Step 1: Create interface**

Create `SmallEBot/Services/Terminal/ITerminalConfigService.cs`:

```csharp
namespace SmallEBot.Services.Terminal;

public interface ITerminalConfigService
{
    /// <summary>Returns the current command blacklist (from file or default). Used by ExecuteCommand tool.</summary>
    IReadOnlyList<string> GetCommandBlacklist();

    /// <summary>Loads the command blacklist for the UI. Returns file content or default if file missing.</summary>
    Task<IReadOnlyList<string>> GetCommandBlacklistAsync(CancellationToken ct = default);

    /// <summary>Persists the command blacklist to .agents/terminal.json.</summary>
    Task SaveCommandBlacklistAsync(IReadOnlyList<string> commandBlacklist, CancellationToken ct = default);
}
```

**Step 2: Add default blacklist constant and file path**

In `TerminalConfigService.cs`, define the config file name and default list (see design doc section 2). Use a static readonly list of strings for the default blacklist (Linux/mac/Windows entries as in the design).

**Step 3: Implement TerminalConfigService**

Create `SmallEBot/Services/Terminal/TerminalConfigService.cs`:

- Constructor: take `ILogger<TerminalConfigService>` (optional for first version).
- `_agentsPath` = `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents")`, `_filePath` = `Path.Combine(_agentsPath, "terminal.json")`.
- Default blacklist: `private static readonly IReadOnlyList<string> DefaultBlacklist = new[] { "rm -rf /", "rm -rf /*", ":(){", "mkfs.", "dd if=", ">/dev/sd", "chmod -R 777 /", "chown -R", "wget -O-", "curl | sh", "format ", "del /s /q", "rd /s /q", "format c:", "format d:", "shutdown /", "reg delete", "sudo " };`
- `GetCommandBlacklist()`: if `!File.Exists(_filePath)` return `DefaultBlacklist`. Else read file synchronously with `File.ReadAllText(_filePath)`, deserialize JSON to a type with `commandBlacklist` property (e.g. `TerminalConfigFile { List<string> CommandBlacklist }`), return the list or `DefaultBlacklist` on error.
- `GetCommandBlacklistAsync(ct)`: if `!File.Exists(_filePath)` return `Task.FromResult((IReadOnlyList<string>)DefaultBlacklist)`. Else `await File.ReadAllTextAsync(_filePath, ct)`, deserialize, return list or default.
- `SaveCommandBlacklistAsync(list, ct)`: `Directory.CreateDirectory(_agentsPath)`, serialize `new { commandBlacklist = list }` to JSON (camelCase, indented), `await File.WriteAllTextAsync(_filePath, json, ct)`.
- Use `System.Text.Json` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and `PropertyNameCaseInsensitive = true` for reading.

**Step 4: Build**

From repo root:

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeds.

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Terminal/ITerminalConfigService.cs SmallEBot/Services/Terminal/TerminalConfigService.cs
git commit -m "feat(terminal): add ITerminalConfigService and TerminalConfigService with default blacklist"
```

---

## Task 2: Register ITerminalConfigService in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add using and registration**

Add:

```csharp
using SmallEBot.Services.Terminal;
```

After MCP/Skills registration, add:

```csharp
services.AddSingleton<ITerminalConfigService, TerminalConfigService>();
```

Keep `IBuiltInToolFactory` as Singleton; it will later depend on `ITerminalConfigService` (Singleton is OK).

**Step 2: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeds.

**Step 3: Commit**

```powershell
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "chore(di): register ITerminalConfigService as Singleton"
```

---

## Task 3: Terminal config dialog UI

**Files:**
- Create: `SmallEBot/Components/Terminal/TerminalConfigDialog.razor`

**Step 1: Create dialog component**

Create `SmallEBot/Components/Terminal/TerminalConfigDialog.razor`:

- Inject: `ITerminalConfigService`, `ISnackbar`, `IDialogService` (for Close).
- State: `_loading`, `_entries` (List<string>), `_newEntry` (string for add box).
- `OnInitializedAsync`: set `_loading = true`, call `_entries = (await TerminalConfig.GetCommandBlacklistAsync()).ToList()`, set `_loading = false`, `StateHasChanged`.
- UI: MudDialog with title "Terminal config". Content: section "Command blacklist". When loading, show MudProgressLinear. Else: MudTable or MudList of `_entries`, each row has the entry text and a Remove MudIconButton (remove from `_entries`). Below: MudTextField for new entry, MudButton "Add" — on click, if `!_newEntry.IsNullOrWhiteSpace()` and `!_entries.Contains(_newEntry.Trim(), StringComparer.OrdinalIgnoreCase)`, add `_newEntry.Trim()` to `_entries`, clear `_newEntry`. Actions: MudButton "Save" (primary), "Cancel".
- Save: call `await TerminalConfig.SaveCommandBlacklistAsync(_entries)`, Snackbar "Saved", then `DialogService.Close(true)` or similar to close dialog.
- Cancel: close without saving.
- All user-facing text in English (e.g. "Command blacklist", "Add", "Remove", "Save", "Cancel", "Saved").

**Step 2: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeds.

**Step 3: Commit**

```powershell
git add SmallEBot/Components/Terminal/TerminalConfigDialog.razor
git commit -m "feat(terminal): add TerminalConfigDialog for editing command blacklist"
```

---

## Task 4: App bar — Terminal config button and open dialog

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`

**Step 1: Add using and inject**

Add `@using SmallEBot.Components.Terminal` (or ensure namespace is available via _Imports). No extra inject needed if IDialogService is already injected.

**Step 2: Add toolbar button**

After the Skills config button (MudTooltip "Skills config"), add:

```razor
<MudTooltip Text="Terminal config">
    <MudIconButton Icon="@Icons.Material.Filled.Terminal"
                   Color="Color.Default"
                   OnClick="@OpenTerminalConfig" />
</MudTooltip>
```

If `Icons.Material.Filled.Terminal` does not exist, use an alternative (e.g. `Icons.Material.Filled.Code` or `Icons.Material.Filled.Console`).

**Step 3: Add method**

In `@code`, add:

```csharp
private void OpenTerminalConfig() =>
    DialogService.ShowAsync<TerminalConfigDialog>(string.Empty, new DialogOptions { MaxWidth = MaxWidth.Medium });
```

**Step 4: Build and verify**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Run the app, click Terminal config in the app bar, confirm the dialog opens and shows the default blacklist (or empty if file not yet created). Add an entry, Save, then reopen and confirm it persisted.

**Step 5: Commit**

```powershell
git add SmallEBot/Components/Layout/MainLayout.razor
git commit -m "feat(terminal): add Terminal config button and open TerminalConfigDialog from App bar"
```

---

## Task 5: ExecuteCommand tool — process runner and blacklist check

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Add dependency and make factory stateful**

- Add `using System.Runtime.InteropServices;` and `using SmallEBot.Services.Terminal;`.
- Change `BuiltInToolFactory` to take constructor: `BuiltInToolFactory(ITerminalConfigService terminalConfig)`.
- Store `_terminalConfig = terminalConfig` as a readonly field.

**Step 2: Add ExecuteCommand instance method**

Add a private instance method that will be the tool handler:

- Signature: `string ExecuteCommand(string command, string? workingDirectory = null)`.
- Normalize command: `var normalized = string.Join(" ", (command ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));` (or simpler: just `(command ?? "").Trim()` for normalization).
- Get blacklist: `var blacklist = _terminalConfig.GetCommandBlacklist();`.
- Check: `if (blacklist.Any(b => normalized.Contains(b, StringComparison.OrdinalIgnoreCase))) return "Error: Command is not allowed by terminal blacklist.";`
- Resolve working dir: baseDir = `Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)`. If `workingDirectory` is null or whitespace, use baseDir. Else combine baseDir with workingDirectory, get full path, and ensure it `StartsWith(baseDir)`; otherwise return "Error: Working directory must be under the run directory."
- Process: use `ProcessStartInfo`. On Windows (`RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`): FileName = `"cmd.exe"`, Arguments = `"/c \"<command>\""` (escape the command for cmd). On Linux/macOS: FileName = `"/bin/sh"`, Arguments = `"-c \"<command>\""`. Set `UseShellExecute = false`, `RedirectStandardOutput = true`, `RedirectStandardError = true`, `WorkingDirectory = resolvedWorkDir`, `CreateNoWindow = true`.
- Start process, read stdout/stderr with `ReadToEndAsync()` or synchronous `ReadToEnd()` on both streams. Wait with timeout 60 seconds (e.g. `process.WaitForExit(60_000)`); if not exited, kill and return "Error: Command timed out after 60 seconds."
- Return string: e.g. `$"ExitCode: {process.ExitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}"`. Handle process start failure with try/catch and return "Error: " + ex.Message.
- Decoration: add `[Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the app run directory. Blocks for up to 60 seconds. Not allowed if the command matches the terminal blacklist.")]` and ensure the method has two parameters so the AI model can pass them.

**Step 3: CreateTools — add ExecuteCommand tool**

Because the tool is an instance method, you cannot use `AIFunctionFactory.Create(ExecuteCommand)` with a static-style delegate. The SDK may allow passing an instance method; if the factory is invoked per-request and the agent is built per-request, the delegate can close over `this`. So in `CreateTools()`:

- Add a fourth tool. For instance methods, use `AIFunctionFactory.Create(ExecuteCommand)` if the compiler allows (the method is on `this`). If the API requires a static method, you will need a different approach: e.g. a static method that takes no args and returns a string, and that is not suitable. So we need the instance method. Check Microsoft.Extensions.AI: `AIFunctionFactory.Create` often accepts `Func<T1, T2, string>` or similar. Use:

```csharp
AIFunctionFactory.Create(ExecuteCommand)
```

If the framework expects a static method, we must obtain `ITerminalConfigService` from a service locator or pass it via a closure. The simplest is: keep an instance method `ExecuteCommand(string command, string? workingDirectory)` and pass `this.ExecuteCommand` to `AIFunctionFactory.Create`. If Create requires a static method, add a static method that takes `(BuiltInToolFactory self, string command, string? workingDirectory)` and call `self.ExecuteCommand(command, workingDirectory)` — but then the factory would need to be resolved at tool invocation time, which is not available if tools are created once. So the intended design is: the tool delegate holds a reference to the factory instance, and the factory holds `ITerminalConfigService`. So when the agent invokes the tool, it calls the instance method on the same factory instance. So use:

```csharp
public AITool[] CreateTools() =>
[
    AIFunctionFactory.Create(GetCurrentTime),
    AIFunctionFactory.Create(ReadFile),
    AIFunctionFactory.Create(ReadSkill),
    AIFunctionFactory.Create(ExecuteCommand)  // instance method
];
```

Ensure `ExecuteCommand` has `[Description("...")]` and parameters are clearly named so the model sees `command` and `workingDirectory`.

**Step 4: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

**Note:** Current built-in tools use static methods with `AIFunctionFactory.Create`. If `Create` does not accept an instance method, create the ExecuteCommand tool manually: instantiate `AITool` (or the SDK’s tool type), set Name = `"ExecuteCommand"`, Description = the same text as above, and the invoke delegate to `(command, workingDirectory) => this.ExecuteCommand(command, workingDirectory)`. Refer to `McpToolFactory` or SDK docs for the exact `AITool` shape.

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(terminal): add ExecuteCommand built-in tool with blacklist and cross-platform process run"
```

---

## Task 6: System prompt — mention ExecuteCommand

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`

**Step 1: Extend base instructions**

In `BaseInstructions` (or the single string used for the first part of the prompt), add a sentence after the existing tool hints, e.g.:

"You can run shell commands on the host with the ExecuteCommand tool (command and optional working directory); avoid commands that are disallowed by the user's terminal blacklist."

Keep the rest of the prompt unchanged.

**Step 2: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/AgentContextFactory.cs
git commit -m "docs(agent): mention ExecuteCommand in system prompt"
```

---

## Task 7: README — Terminal config and ExecuteCommand

**Files:**
- Modify: `SmallEBot/README.md`

**Step 1: Add feature line**

In the Features section, add a bullet:

- **Terminal:** Run shell commands via the agent (ExecuteCommand tool). Command blacklist is configurable in Terminal config (App bar); default blocks common dangerous commands.

**Step 2: Commit**

```powershell
git add SmallEBot/README.md
git commit -m "docs(readme): add Terminal and ExecuteCommand to features"
```

---

## Verification checklist (after all tasks)

- Build: `dotnet build SmallEBot/SmallEBot.csproj` succeeds.
- Run: `dotnet run --project SmallEBot`. Open app, click Terminal config: dialog opens, default blacklist shown (or empty list if first run and file not saved yet). Add entry, Save, reopen — list persisted. Remove entry, Save, reopen — list updated.
- Chat: Ask the agent to run a safe command (e.g. "Run the command: dotnet --version"). Agent should call ExecuteCommand and return output. Ask to run a blacklisted command (e.g. "Run: rm -rf /"); agent should get blacklist error and report it.
- Working directory: Ask to run `git status` with working directory set to repo root (or a subfolder under run directory); confirm output is correct.

---

## Execution options

Plan complete and saved to `docs/plans/2026-02-16-terminal-command-execution-implementation-plan.md`.

**Two execution options:**

1. **Subagent-driven (this session)** — I execute each task in order, run build/verify, and commit; you review between tasks.
2. **Parallel session (separate)** — You open a new session (optionally in a worktree), use the executing-plans skill, and run through the plan with checkpoints.

Which approach do you want?
