# MCP Configuration UI and File-Based Config — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a toolbar MCP config button that opens a dialog to view/edit MCP (http/stdio), with config under `.agents` (`.sys.mcp.json` + `.mcp.json`), enable/disable persisted in settings, and optional display of tools/prompts per MCP.

**Architecture:** New `McpConfigService` reads/writes `.agents/.sys.mcp.json` (read-only) and `.agents/.mcp.json` (user). `UserPreferencesService` gains `DisabledMcpIds` in `SmallEBotSettings`. `AgentService` loads MCP from McpConfigService filtered by disabled set. UI: MainLayout AppBar button → MudDialog with list (system + user), enable switch, add/edit wizard; expandable row for tools/prompts when API available.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, ModelContextProtocol.Client, `IWebHostEnvironment`, existing `UserPreferencesService` / `AgentService` patterns.

**Reference:** Design: `docs/plans/2026-02-15-mcp-config-design.md`. No test project in repo; use `dotnet build` and manual/browser verification per task.

---

## Task 1: Models for MCP config and disabled list

**Files:**
- Create: `SmallEBot/Models/McpServerEntry.cs`
- Modify: `SmallEBot/Models/SmallEBotSettings.cs`

**Step 1: Add MCP config DTOs**

Create `SmallEBot/Models/McpServerEntry.cs`:

```csharp
namespace SmallEBot.Models;

/// <summary>Single MCP server definition (http or stdio).</summary>
public sealed class McpServerEntry
{
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public Dictionary<string, string?>? Env { get; set; }
}
```

**Step 2: Extend SmallEBotSettings with DisabledMcpIds**

In `SmallEBot/Models/SmallEBotSettings.cs`, add property:

```csharp
public List<string> DisabledMcpIds { get; set; } = [];
```

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeded.

```bash
git add SmallEBot/Models/McpServerEntry.cs SmallEBot/Models/SmallEBotSettings.cs
git commit -m "feat(mcp): add McpServerEntry model and DisabledMcpIds in settings"
```

---

## Task 2: UserPreferencesService — get/set DisabledMcpIds

**Files:**
- Modify: `SmallEBot/Services/UserPreferencesService.cs`

**Step 1: Add SetDisabledMcpIdsAsync and ensure Load/Save handle the new property**

- In `UserPreferencesService`, add method:

```csharp
/// <summary>Update DisabledMcpIds and persist.</summary>
public async Task SetDisabledMcpIdsAsync(List<string> ids, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        var current = _cached ?? await LoadInternalAsync(ct);
        if (_cached == null) _cached = current;
        current.DisabledMcpIds = ids ?? new List<string>();
        await SaveInternalAsync(current, ct);
    }
    finally
    {
        _lock.Release();
    }
}
```

- Ensure `SmallEBotSettings` is serialized with `DisabledMcpIds` (already a property; JsonSerializer will include it by default). No change to `LoadAsync` needed if the file lacks the key—deserializer will set default empty list.

**Step 2: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/UserPreferencesService.cs
git commit -m "feat(mcp): add SetDisabledMcpIdsAsync to UserPreferencesService"
```

---

## Task 3: McpConfigService — read .sys.mcp.json and .mcp.json, merge, write .mcp.json

**Files:**
- Create: `SmallEBot/Services/McpConfigService.cs`
- Modify: `SmallEBot/Program.cs`

**Step 1: Define interface and implementation**

Create `SmallEBot/Services/McpConfigService.cs`:

```csharp
using System.Text.Json;
using SmallEBot.Models;

namespace SmallEBot.Services;

public record McpEntryWithSource(string Id, McpServerEntry Entry, bool IsSystem);

public interface IMcpConfigService
{
    string AgentsDirectoryPath { get; }
    Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default);
    Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default);
}

public class McpConfigService : IMcpConfigService
{
    private const string SysFileName = ".sys.mcp.json";
    private const string UserFileName = ".mcp.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _agentsPath;
    private readonly ILogger<McpConfigService> _log;

    public McpConfigService(IWebHostEnvironment env, ILogger<McpConfigService> log)
    {
        _agentsPath = Path.Combine(env.ContentRootPath, ".agents");
        _log = log;
    }

    public string AgentsDirectoryPath => _agentsPath;

    public async Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default)
    {
        var system = await LoadJsonAsync(Path.Combine(_agentsPath, SysFileName), ct);
        var user = await LoadJsonAsync(Path.Combine(_agentsPath, UserFileName), ct);
        var list = new List<McpEntryWithSource>();
        foreach (var kv in system)
            list.Add(new McpEntryWithSource(kv.Key, kv.Value, IsSystem: true));
        foreach (var kv in user)
        {
            if (system.ContainsKey(kv.Key)) continue;
            list.Add(new McpEntryWithSource(kv.Key, kv.Value, IsSystem: false));
        }
        return list;
    }

    public async Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default)
    {
        var user = await LoadJsonAsync(Path.Combine(_agentsPath, UserFileName), ct);
        return user;
    }

    public async Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_agentsPath);
        var path = Path.Combine(_agentsPath, UserFileName);
        var dict = userMcp.ToDictionary(k => k.Key, v => v.Value);
        var json = JsonSerializer.Serialize(dict, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private async Task<Dictionary<string, McpServerEntry>> LoadJsonAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new Dictionary<string, McpServerEntry>();
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var dict = JsonSerializer.Deserialize<Dictionary<string, McpServerEntry>>(json);
            return dict ?? new Dictionary<string, McpServerEntry>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load MCP config from {Path}", path);
            return new Dictionary<string, McpServerEntry>();
        }
    }
}
```

**Step 2: Register in Program.cs**

In `SmallEBot/Program.cs`, after `AddScoped<UserPreferencesService>()` add:

```csharp
builder.Services.AddScoped<IMcpConfigService, McpConfigService>();
```

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/McpConfigService.cs SmallEBot/Program.cs
git commit -m "feat(mcp): add McpConfigService for .agents config read/write"
```

---

## Task 4: AgentService — load MCP from McpConfigService, filter by DisabledMcpIds

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`

**Step 1: Inject IMcpConfigService and UserPreferencesService**

- Add to `AgentService` constructor parameters: `IMcpConfigService mcpConfig`, `UserPreferencesService userPrefs`.
- Remove use of `config.GetSection("mcpServers")` in `EnsureToolsAsync`.

**Step 2: Replace MCP loading logic in EnsureToolsAsync**

- Call `await mcpConfig.GetAllAsync(ct)`.
- Load preferences: `var prefs = await userPrefs.LoadAsync(ct); var disabled = prefs.DisabledMcpIds ?? new List<string>();`
- Loop over `GetAllAsync` result; if `entry.Id` is in `disabled`, skip.
- For each entry, if stdio (no type but has Command, or type "stdio"): use `StdioClientTransport` with Name = entry.Id, Command = entry.Entry.Command, Arguments = entry.Entry.Args ?? [], EnvironmentVariables = entry.Entry.Env ?? new Dictionary<string, string?>(). If http (type "http"): use `HttpClientTransport` with Endpoint = entry.Entry.Url. Then `McpClient.CreateAsync(transport, null, null, ct)`, `ListToolsAsync`, add to tools. On exception, log and continue.

**Step 3: Build and run quick smoke test**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Run: `dotnet run --project SmallEBot` (or start from IDE). Open app, ensure chat still works and MCP tools load (e.g. ask for time; if microsoft.docs.mcp is enabled, a docs query might trigger tool). Stop app.

```bash
git add SmallEBot/Services/AgentService.cs
git commit -m "refactor(mcp): load MCP from McpConfigService and filter by DisabledMcpIds"
```

---

## Task 5: Toolbar button and dialog shell

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Create: `SmallEBot/Components/Mcp/McpConfigDialog.razor`

**Step 1: Add AppBar button in MainLayout**

In `MainLayout.razor`, inject `IDialogService` and add before the username text (e.g. before `<MudText ... @UserNameSvc.CurrentDisplayName>`):

```razor
<MudTooltip Text="MCP 配置">
    <MudIconButton Icon="@Icons.Material.Filled.Extension"
                   Color="Color.Default"
                   OnClick="@OpenMcpConfig" />
</MudTooltip>
```

In `@code`, add:

```csharp
[Inject] private IDialogService DialogService { get; set; } = null!;

private void OpenMcpConfig() =>
    DialogService.ShowAsync<SmallEBot.Components.Mcp.McpConfigDialog>(string.Empty, new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
```

**Step 2: Create minimal MCP config dialog**

Create `SmallEBot/Components/Mcp/McpConfigDialog.razor`:

```razor
<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">MCP 配置</MudText>
    </TitleContent>
    <DialogContent>
        <MudText>MCP list will be loaded here.</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Close">关闭</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;
    private void Close() => MudDialog.Close();
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`. Run app, click the new MCP icon in the AppBar; dialog opens with placeholder text. Close dialog.

```bash
git add SmallEBot/Components/Layout/MainLayout.razor SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "feat(mcp): add toolbar MCP config button and dialog shell"
```

---

## Task 6: MCP list in dialog — load and show system/user, enable switch

**Files:**
- Modify: `SmallEBot/Components/Mcp/McpConfigDialog.razor`

**Step 1: Inject services and load data**

- Inject `IMcpConfigService`, `UserPreferencesService`, `ISnackbar`.
- OnInitializedAsync: call `McpConfigService.GetAllAsync()` and `UserPreferencesService.LoadAsync()`. Store list and `DisabledMcpIds` in fields. Build a view model list: Id, Entry, IsSystem, IsEnabled = !DisabledMcpIds.Contains(Id).

**Step 2: Render list**

- Use `MudTable` with columns: Name (Id), Type (http/stdio), Info (URL or command), Source (系统/用户), Enable (`MudSwitch` bound to IsEnabled). For system rows, do not show edit/delete. For user rows, add Edit and Delete buttons (handlers can be empty for now).
- Add “新增 MCP” button above the table.

**Step 3: Wire enable switch**

- On switch change: update local view model and call `UserPreferencesService.SetDisabledMcpIdsAsync` with the new list (if enabled, remove id from disabled; if disabled, add id). Optionally invalidate Agent (e.g. next request will rebuild); design says “next Agent build” so no need to restart app if Agent is built per-request or on next chat).

**Step 4: Build and verify**

Run: `dotnet build`, run app, open MCP dialog. List shows entries from `.sys.mcp.json` (and `.mcp.json` if present). Toggle a switch; close and reopen dialog or reload settings and confirm the switch state persisted.

```bash
git add SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "feat(mcp): show MCP list with system/user and enable switch"
```

---

## Task 7: Add/Edit user MCP — wizard (type, then HTTP or Stdio fields)

**Files:**
- Modify: `SmallEBot/Components/Mcp/McpConfigDialog.razor` (or create sub-component for the form)
- Optionally create: `SmallEBot/Components/Mcp/McpEditForm.razor`

**Step 1: Add dialog state for “adding” or “editing”**

- State: `_editingId` (null = list view, non-null = editing that id) and `_wizardStep` (1 = choose type, 2 = fill fields). Fields: `_editId`, `_editType` (http/stdio), `_editUrl`, `_editCommand`, `_editArgs` (string or list), `_editEnv` (optional). “新增 MCP” sets _editingId to empty string (new), _wizardStep = 1. “Edit” on a user row sets _editingId to that id, load entry into fields, _wizardStep = 1 or 2.

**Step 2: Wizard UI**

- When _editingId != null, show Stepper or two steps: Step 1 — MudRadioGroup for type (HTTP / Stdio). Step 2 — if HTTP: MudTextField for Id (name), Url. If Stdio: Id, Command, Args (e.g. comma-separated or repeatable), optional Env. Buttons: 上一步 / 下一步, 保存 / 取消.
- On Save: build `McpServerEntry`, get current user dict via `McpConfigService.GetUserMcpAsync()`, merge new/updated entry, call `McpConfigService.SaveUserMcpAsync`. Close wizard (clear _editingId), refresh list.

**Step 3: Delete user MCP**

- Delete button: remove id from user dict, call SaveUserMcpAsync, refresh list.

**Step 4: Build and verify**

Run: `dotnet build`. Run app, open MCP dialog. Add new MCP (e.g. HTTP with a test URL), save. Confirm it appears in list and in `.agents/.mcp.json`. Edit it, then delete it; confirm file and list update.

```bash
git add SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "feat(mcp): add/edit/delete user MCP with wizard"
```

---

## Task 8: Expandable row — tools (and prompts if API available)

**Files:**
- Modify: `SmallEBot/Components/Mcp/McpConfigDialog.razor`

**Step 1: Per-MCP tools/prompts loading**

- When dialog opens (or when user expands a row), for each enabled MCP try to create transport (same logic as AgentService: stdio/http from entry), `McpClient.CreateAsync`, then call `ListToolsAsync`. If the MCP client package exposes `ListPromptsAsync`, call it and store prompts; otherwise skip prompts or show “—” for that section.
- Store result in a dictionary keyed by MCP id: `Dictionary<string, (IReadOnlyList<Tool> Tools, IReadOnlyList<Prompt>? Prompts)>`. Load on dialog open for all enabled entries (or on first expand per row to avoid blocking). On failure, store null or error message for that id.

**Step 2: Expandable row content**

- Use `MudTable` with `MudTableRow` that contains an expand row: when expanded, show for that MCP: “工具” section (tool name + description for each), “提示词” section (prompt name + description if available). If load failed, show “加载失败”.

**Step 3: Build and verify**

Run: `dotnet build`. Run app, open MCP dialog, expand an MCP row. Confirm tools (and prompts if API exists) appear. If ListPromptsAsync is not in the package, document in code that prompts are skipped until the client supports it.

```bash
git add SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "feat(mcp): show tools and prompts per MCP in expandable row"
```

---

## Task 9: Error handling and edge cases

**Files:**
- Modify: `SmallEBot/Services/McpConfigService.cs`, `SmallEBot/Components/Mcp/McpConfigDialog.razor`

**Step 1: McpConfigService**

- Ensure missing `.agents` or missing files return empty list (already done). On SaveUserMcpAsync failure, throw or return bool; caller (dialog) shows Snackbar.

**Step 2: Dialog**

- On load list failure: show Snackbar “加载 MCP 配置失败”. On save user MCP failure: show Snackbar “保存失败”. Duplicate id (user adds id that exists in system): prefer showing in “user” list as editable and allow overwrite in user file, or prevent duplicate id in wizard—design says “prefer system for Agent”, so in UI allow user to have same id but AgentService already prefers system. Easiest: prevent adding user entry with id that exists in system (validation in wizard).

**Step 3: Commit**

```bash
git add SmallEBot/Services/McpConfigService.cs SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "fix(mcp): error handling and duplicate-id validation in dialog"
```

---

## Task 10: Final verification and docs

**Files:**
- Modify: `docs/plans/2026-02-15-mcp-config-design.md` (set Status to Implemented if desired)

**Step 1: Full flow verification**

- `dotnet build SmallEBot/SmallEBot.csproj`. Run app. Open MCP dialog: system MCPs from `.sys.mcp.json` and user from `.mcp.json`. Toggle enable/disable, add/edit/delete user MCP. Expand row and confirm tools (and prompts if supported). Disable an MCP, send a chat message, confirm no crash and agent uses remaining MCPs.

**Step 2: Update design doc status**

- In `docs/plans/2026-02-15-mcp-config-design.md`, change **Status** to `Implemented` and add a short “Implementation” line referencing this plan and date.

**Step 3: Commit**

```bash
git add docs/plans/2026-02-15-mcp-config-design.md
git commit -m "docs: mark MCP config design as implemented"
```

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-15-mcp-config-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** — Dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** — Open a new session with executing-plans and run through the plan with checkpoints.

Which approach do you want to use?
