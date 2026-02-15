# Skills (Local File-Based) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add file-based skills: metadata in system prompt (progressive disclosure), ReadFile tool for content, SkillsConfigService and UI (list, add, import, delete). Only directories with valid SKILL.md frontmatter are loaded.

**Architecture:** `SkillsConfigService` uses `BaseDirectory + ".agents"`; enumerates `.agents/sys.skills/` and `.agents/skills/`, parses SKILL.md frontmatter (name, description). No frontmatter = not loaded. AgentService gets skill metadata, appends skills block to instructions, registers ReadFile tool (general-purpose: path relative to run directory, allowed extensions only). UI: toolbar button → dialog with table (system + user), add form, import (folder), delete for user skills. System skills dir shipped via Content copy.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, existing AgentService/McpConfigService patterns. No new NuGet; frontmatter parsed with simple string logic (no YAML lib).

**Reference:** Design: `docs/plans/2026-02-15-skills-design.md`. No test project; use `dotnet build` and manual/browser verification per task.

---

## Task 1: Skill metadata model and frontmatter parsing

**Files:**
- Create: `SmallEBot/Models/SkillMetadata.cs`
- Create: `SmallEBot/Services/SkillFrontmatterParser.cs`

**Step 1: Add SkillMetadata record**

Create `SmallEBot/Models/SkillMetadata.cs`:

```csharp
namespace SmallEBot.Models;

/// <summary>Skill metadata from SKILL.md frontmatter. Only skills with valid frontmatter are loaded.</summary>
public sealed record SkillMetadata(string Id, string Name, string Description, bool IsSystem);
```

**Step 2: Add frontmatter parser (no YAML package)**

Create `SmallEBot/Services/SkillFrontmatterParser.cs`:

```csharp
namespace SmallEBot.Services;

/// <summary>Parses name and description from SKILL.md YAML frontmatter. Returns null if missing or invalid.</summary>
public static class SkillFrontmatterParser
{
    private const string StartFence = "---";
    private const string NameKey = "name:";
    private const string DescriptionKey = "description:";

    /// <summary>Try parse frontmatter from file content. Returns (name, description) or null if invalid.</summary>
    public static (string Name, string Description)? TryParse(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent)) return null;
        var lines = fileContent.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != StartFence) return null;
        string? name = null, description = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == StartFence) break;
            if (line.StartsWith(NameKey, StringComparison.OrdinalIgnoreCase))
                name = line[NameKey.Length..].Trim().Trim('"').Trim('\'');
            else if (line.StartsWith(DescriptionKey, StringComparison.OrdinalIgnoreCase))
                description = line[DescriptionKey.Length..].Trim().Trim('"').Trim('\'');
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)) return null;
        return (name, description);
    }
}
```

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeded.

```bash
git add SmallEBot/Models/SkillMetadata.cs SmallEBot/Services/SkillFrontmatterParser.cs
git commit -m "feat(skills): add SkillMetadata and SkillFrontmatterParser"
```

---

## Task 2: SkillsConfigService — list and get metadata

**Files:**
- Create: `SmallEBot/Services/SkillsConfigService.cs`
- Modify: `SmallEBot/Program.cs`

**Step 1: Implement SkillsConfigService (list + get metadata only)**

Create `SmallEBot/Services/SkillsConfigService.cs`:

```csharp
using SmallEBot.Models;

namespace SmallEBot.Services;

public interface ISkillsConfigService
{
    string AgentsDirectoryPath { get; }
    Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default);
}

public class SkillsConfigService : ISkillsConfigService
{
    private const string SysSkillsDir = "sys.skills";
    private const string UserSkillsDir = "skills";
    private const string SkillFileName = "SKILL.md";

    private readonly string _agentsPath;
    private readonly ILogger<SkillsConfigService> _log;

    public SkillsConfigService(ILogger<SkillsConfigService> log)
    {
        _agentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");
        _log = log;
    }

    public string AgentsDirectoryPath => _agentsPath;

    public Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    public Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    private async Task<IReadOnlyList<SkillMetadata>> GetMetadataInternalAsync(bool includeSystem, bool includeUser, CancellationToken ct)
    {
        var list = new List<SkillMetadata>();
        if (includeSystem)
        {
            var sysPath = Path.Combine(_agentsPath, SysSkillsDir);
            await AddSkillsFromDirectoryAsync(sysPath, isSystem: true, list, ct);
        }
        if (includeUser)
        {
            var userPath = Path.Combine(_agentsPath, UserSkillsDir);
            await AddSkillsFromDirectoryAsync(userPath, isSystem: false, list, ct);
        }
        return list;
    }

    private async Task AddSkillsFromDirectoryAsync(string parentDir, bool isSystem, List<SkillMetadata> list, CancellationToken ct)
    {
        if (!Directory.Exists(parentDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(parentDir))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(dir);
            var skillPath = Path.Combine(dir, SkillFileName);
            if (!File.Exists(skillPath)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(skillPath, ct);
                var parsed = SkillFrontmatterParser.TryParse(content);
                if (parsed == null) continue;
                list.Add(new SkillMetadata(Id: id, parsed.Value.Name, parsed.Value.Description, IsSystem: isSystem));
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skip skill dir {Dir}: no valid frontmatter", dir);
            }
        }
    }
}
```

**Step 2: Register in Program.cs**

In `SmallEBot/Program.cs`, after `AddScoped<IMcpConfigService, McpConfigService>()` add:

```csharp
builder.Services.AddScoped<ISkillsConfigService, SkillsConfigService>();
```

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/SkillsConfigService.cs SmallEBot/Program.cs
git commit -m "feat(skills): add SkillsConfigService list and get metadata"
```

---

## Task 3: ReadFile tool and AgentService integration

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`

**Step 1: Define allowed extensions and ReadFile implementation**

In `AgentService.cs`, add after the constructor and before `EnsureToolsAsync`:

- Add constructor parameter: `ISkillsConfigService skillsConfig`.
- Add private static allowlist: `private static readonly HashSet<string> ReadFileAllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml" };`.
- Add private method ReadFile: path is relative to run directory (`AppDomain.CurrentDomain.BaseDirectory`). Resolve fullPath = Path.GetFullPath(Path.Combine(BaseDirectory, path)). Ensure fullPath.StartsWith(BaseDirectory) so no `..` escape. If extension not in allowlist or file missing, return error; else return File.ReadAllText(fullPath).

**Step 2: ReadFile tool implementation (exact code)**

Add to `AgentService` (after `GetCurrentTime`):

```csharp
private static readonly HashSet<string> ReadFileAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml"
};

[Description("Read a text file under the current run directory. Pass path relative to the app directory (e.g. .agents/sys.skills/weekly-report-generator/SKILL.md or .agents/skills/my-skill/script.py). Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.")]
private string ReadFile(string path)
{
    if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
    var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
    var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        return "Error: path must be under the current run directory.";
    var ext = Path.GetExtension(fullPath);
    if (string.IsNullOrEmpty(ext) || !ReadFileAllowedExtensions.Contains(ext))
        return "Error: file type not allowed. Allowed: " + string.Join(", ", ReadFileAllowedExtensions);
    if (!File.Exists(fullPath))
        return "Error: file not found.";
    try
    {
        return File.ReadAllText(fullPath);
    }
    catch (Exception ex)
    {
        return "Error: " + ex.Message;
    }
}
```

**Step 3: EnsureToolsAsync — add ReadFile to tools**

In `EnsureToolsAsync`, change the first line from:

```csharp
var tools = new List<AITool> { AIFunctionFactory.Create(GetCurrentTime) };
```

to:

```csharp
var tools = new List<AITool>
{
    AIFunctionFactory.Create(GetCurrentTime),
    AIFunctionFactory.Create(ReadFile)
};
```

**Step 4: Build instructions with skills block**

- Add a method that takes `IReadOnlyList<SkillMetadata>` and returns the skills block string (intro line + one line per skill: id, name, description).
- In `EnsureAgentAsync`, before creating the agent: call `await skillsConfig.GetMetadataForAgentAsync(ct)`, build full instructions = `AgentInstructions + "\n\n" + skillsBlock`. Use that as `instructions` when calling `AsAIAgent(...)`.

Example block builder:

```csharp
private static string BuildSkillsBlock(IReadOnlyList<SkillMetadata> skills)
{
    if (skills.Count == 0) return "";
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("You have access to the following skills. Each has an id and a short description. To read a skill's content, use the ReadFile tool with a path relative to the run directory (e.g. .agents/sys.skills/<id>/SKILL.md or .agents/skills/<id>/...). ReadFile can read any file under the run directory with allowed extensions (.md, .cs, .py, .txt, .json, .yml, .yaml).");
    sb.AppendLine();
    foreach (var s in skills)
        sb.AppendLine($"- {s.Id}: {s.Name} — {s.Description}");
    return sb.ToString();
}
```

Then in `EnsureAgentAsync`, after getting tools:

```csharp
var skillList = await skillsConfig.GetMetadataForAgentAsync(ct);
var skillsBlock = BuildSkillsBlock(skillList);
var instructions = string.IsNullOrEmpty(skillsBlock) ? AgentInstructions : AgentInstructions + "\n\n" + skillsBlock;
```

Use `instructions` instead of `AgentInstructions` in both `AsAIAgent` calls.

**Step 5: InvalidateAgentAsync**

Ensure `InvalidateAgentAsync` is called when skills change; no extra cache in AgentService for skills (metadata is read each time on EnsureAgentAsync). So no code change in InvalidateAgentAsync for this task.

**Step 6: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/AgentService.cs
git commit -m "feat(skills): add ReadFile tool and skills block to agent instructions"
```

---

## Task 4: SkillsConfigService — add, delete, import

**Files:**
- Modify: `SmallEBot/Services/SkillsConfigService.cs`

**Step 1: Add user skill (create directory + SKILL.md)**

Add to interface:

```csharp
Task AddUserSkillAsync(string id, string name, string description, CancellationToken ct = default);
```

Implement: sanitize id (replace invalid path chars with underscore), ensure `.agents/skills` exists, create `.agents/skills/{id}`, write SKILL.md with frontmatter:

```markdown
---
name: {name}
description: {description}
---

# {name}
```

Escape name/description if they contain `---` or newlines (keep on one line for description; name single line).

**Step 2: Delete user skill**

Add to interface:

```csharp
Task DeleteUserSkillAsync(string id, CancellationToken ct = default);
```

Implement: resolve path = `.agents/skills/{id}`. If path is not under user skills dir (security), throw. If directory doesn't exist, return. Delete directory recursively. Only allow deletion under `skills/`, not `sys.skills/`.

**Step 3: Import skill from folder**

Add to interface:

```csharp
Task ImportUserSkillFromFolderAsync(string sourceFolderPath, string? id = null, CancellationToken ct = default);
```

Implement: id = id ?? Path.GetFileName(Path.TrimEndDirectorySeparator(sourceFolderPath)). Sanitize id. Ensure `.agents/skills` exists. destDir = `.agents/skills/{id}`. If destDir exists, delete or overwrite (design: overwrite with confirmation in UI; here implement overwrite). Copy all files from sourceFolderPath to destDir. If no SKILL.md in source, copy still runs but skill won't load until user adds frontmatter (or fail import—design says "validate at least SKILL.md exists"; so after copy, if no SKILL.md in destDir, optionally delete destDir and throw "SKILL.md is required". Design: "Validate at least SKILL.md exists". So after copy, check File.Exists(Path.Combine(destDir, "SKILL.md")); if not, delete destDir and throw InvalidOperationException("Source folder must contain SKILL.md").)

**Step 4: Implementation code (add/delete/import)**

Add to `SkillsConfigService.cs`:

```csharp
public async Task AddUserSkillAsync(string id, string name, string description, CancellationToken ct = default)
{
    var safeId = SanitizeSkillId(id);
    var userPath = Path.Combine(_agentsPath, UserSkillsDir);
    Directory.CreateDirectory(userPath);
    var skillDir = Path.Combine(userPath, safeId);
    if (Directory.Exists(skillDir))
        throw new InvalidOperationException($"Skill id already exists: {safeId}");
    Directory.CreateDirectory(skillDir);
    var content = $@"---
name: {EscapeFrontmatterValue(name)}
description: {EscapeFrontmatterValue(description)}
---

# {name}
";
    await File.WriteAllTextAsync(Path.Combine(skillDir, SkillFileName), content, ct);
}

public async Task DeleteUserSkillAsync(string id, CancellationToken ct = default)
{
    var userPath = Path.Combine(_agentsPath, UserSkillsDir);
    var skillDir = Path.GetFullPath(Path.Combine(userPath, id));
    var userRoot = Path.GetFullPath(userPath);
    if (!skillDir.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase) || skillDir.Length <= userRoot.Length)
        throw new UnauthorizedAccessException("Cannot delete system or invalid skill.");
    if (Directory.Exists(skillDir))
        Directory.Delete(skillDir, recursive: true);
}

public async Task ImportUserSkillFromFolderAsync(string sourceFolderPath, string? id = null, CancellationToken ct = default)
{
    var src = Path.GetFullPath(sourceFolderPath);
    if (!Directory.Exists(src))
        throw new DirectoryNotFoundException($"Source folder not found: {src}");
    var skillId = id ?? Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    var safeId = SanitizeSkillId(skillId);
    var userPath = Path.Combine(_agentsPath, UserSkillsDir);
    Directory.CreateDirectory(userPath);
    var destDir = Path.Combine(userPath, safeId);
    if (Directory.Exists(destDir))
        Directory.Delete(destDir, recursive: true);
    CopyDirectory(src, destDir);
    var skillPath = Path.Combine(destDir, SkillFileName);
    if (!File.Exists(skillPath))
    {
        Directory.Delete(destDir, recursive: true);
        throw new InvalidOperationException("Source folder must contain SKILL.md.");
    }
    await Task.CompletedTask;
}

private static string SanitizeSkillId(string id)
{
    var invalid = Path.GetInvalidFileNameChars();
    var s = string.Join("_", id.Trim().Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    return string.IsNullOrEmpty(s) ? "skill" : s;
}

private static string EscapeFrontmatterValue(string v)
{
    if (string.IsNullOrEmpty(v)) return "";
    var one = v.Replace("\r", " ").Replace("\n", " ").Trim();
    if (one.Contains('"')) return "'" + one.Replace("'", "''") + "'";
    return one.Length > 80 ? "\"" + one[..80] + "\"" : one;
}

private static void CopyDirectory(string sourceDir, string destDir)
{
    Directory.CreateDirectory(destDir);
    foreach (var file in Directory.EnumerateFiles(sourceDir))
        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
    foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
}
```

Add the new methods to the interface at the top of the file.

**Step 5: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/SkillsConfigService.cs
git commit -m "feat(skills): add AddUserSkillAsync, DeleteUserSkillAsync, ImportUserSkillFromFolderAsync"
```

---

## Task 5: Toolbar button and SkillsConfigDialog shell

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Create: `SmallEBot/Components/Skills/SkillsConfigDialog.razor`

**Step 1: Add Skills button in MainLayout**

In `MainLayout.razor`, after the MCP config tooltip block (after `</MudTooltip>` for MCP), add:

```razor
<MudTooltip Text="Skills 配置">
    <MudIconButton Icon="@Icons.Material.Filled.School"
                   Color="Color.Default"
                   OnClick="@OpenSkillsConfig" />
</MudTooltip>
```

In @code, add:

```csharp
private void OpenSkillsConfig() =>
    DialogService.ShowAsync<SmallEBot.Components.Skills.SkillsConfigDialog>(string.Empty, new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
```

**Step 2: Create SkillsConfigDialog with list only**

Create `SmallEBot/Components/Skills/SkillsConfigDialog.razor`:

```razor
@inject ISkillsConfigService SkillsConfig
@inject AgentService AgentService
@inject IDialogService DialogService
@inject ISnackbar Snackbar

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Skills 配置</MudText>
    </TitleContent>
    <DialogContent>
        @if (_loading)
        {
            <MudProgressLinear Color="Color.Primary" Indeterminate="true" />
        }
        else
        {
            <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add" OnClick="StartAdd" Class="mb-3">新增 Skill</MudButton>
            <MudButton Variant="Variant.Outlined" Class="mb-3 ms-2" StartIcon="@Icons.Material.Filled.Upload" OnClick="StartImport">导入</MudButton>
            <MudTable Items="@_rows" Hover="true" Breakpoint="Breakpoint.Sm" Dense="true">
                <HeaderContent>
                    <MudTh>名称 (id)</MudTh>
                    <MudTh>描述</MudTh>
                    <MudTh>来源</MudTh>
                    <MudTh Style="width: 100px;">操作</MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="名称">@context.Name (@context.Id)</MudTd>
                    <MudTd DataLabel="描述">@Truncate(context.Description, 60)</MudTd>
                    <MudTd DataLabel="来源">@(context.IsSystem ? "系统" : "用户")</MudTd>
                    <MudTd DataLabel="操作">
                        @if (!context.IsSystem)
                        {
                            <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small" OnClick="@(() => ConfirmDelete(context.Id))" title="删除" />
                        }
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Close">关闭</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    private bool _loading = true;
    private List<SmallEBot.Models.SkillMetadata> _rows = [];

    protected override async Task OnInitializedAsync()
    {
        await LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        _loading = true;
        try
        {
            var all = await SkillsConfig.GetAllAsync();
            _rows = all.ToList();
        }
        catch
        {
            Snackbar.Add("加载 Skills 失败", Severity.Warning);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task StartAdd()
    {
        var dialog = await DialogService.ShowAsync<AddSkillDialog>("新增 Skill", new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result?.Canceled != false || result?.Data is not (string id, string name, string desc)) return;
        await SaveAddAsync(id, name, desc);
    }

    private async Task StartImport()
    {
        // Task 6 will add import dialog; for now show snackbar
        Snackbar.Add("导入功能请在下一任务中完成", Severity.Info);
        await Task.CompletedTask;
    }

    private async Task SaveAddAsync(string id, string name, string desc)
    {
        try
        {
            await SkillsConfig.AddUserSkillAsync(id, name, desc);
            await AgentService.InvalidateAgentAsync();
            Snackbar.Add("已添加", Severity.Success);
            await LoadListAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add("添加失败: " + ex.Message, Severity.Error);
        }
    }

    private async Task ConfirmDelete(string id)
    {
        var parameters = new DialogParameters { [nameof(DeleteSkillConfirmDialog.Id)] = id };
        var dialog = await DialogService.ShowAsync<DeleteSkillConfirmDialog>("删除 Skill", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == true) return;
        if (result?.Data is true)
            await DoDeleteAsync(id);
    }

    private async Task DoDeleteAsync(string id)
    {
        try
        {
            await SkillsConfig.DeleteUserSkillAsync(id);
            await AgentService.InvalidateAgentAsync();
            Snackbar.Add("已删除", Severity.Success);
            await LoadListAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add("删除失败: " + ex.Message, Severity.Error);
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private void Close() => MudDialog.Close();
}
```

**Step 3: Create AddSkillDialog placeholder**

Create `SmallEBot/Components/Skills/AddSkillDialog.razor`:

```razor
@inject ISnackbar Snackbar

<MudDialog>
    <TitleContent><MudText Typo="Typo.h6">新增 Skill</MudText></TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_id" Label="Id (目录名)" Variant="Variant.Outlined" Required RequiredError="必填" Class="mb-2" />
        <MudTextField @bind-Value="_name" Label="名称" Variant="Variant.Outlined" Required RequiredError="必填" Class="mb-2" />
        <MudTextField @bind-Value="_description" Label="描述" Variant="Variant.Outlined" Required RequiredError="必填" Lines="2" />
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Cancel">取消</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="Submit">添加</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;
    private string _id = "";
    private string _name = "";
    private string _description = "";

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(_id) || string.IsNullOrWhiteSpace(_name) || string.IsNullOrWhiteSpace(_description))
        {
            Snackbar.Add("请填写 Id、名称和描述", Severity.Warning);
            return;
        }
        MudDialog.Close(DialogResult.Ok((_id.Trim(), _name.Trim(), _description.Trim())));
    }
}
```

**Step 4: Create DeleteSkillConfirmDialog**

Create `SmallEBot/Components/Skills/DeleteSkillConfirmDialog.razor`:

```razor
<MudDialog>
    <TitleContent><MudText Typo="Typo.h6">删除 Skill</MudText></TitleContent>
    <DialogContent>
        <MudText>确定要删除 Skill「@Id」吗？</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Cancel">取消</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Error" OnClick="Confirm">删除</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter] public string Id { get; set; } = "";

    private void Cancel() => MudDialog.Cancel();
    private void Confirm() => MudDialog.Close(DialogResult.Ok(true));
}
```

Fix SkillsConfigDialog: pass parameter by name. In `ConfirmDelete`, use:

```csharp
var parameters = new DialogParameters { [nameof(DeleteSkillConfirmDialog.Id)] = id };
```

**Step 5: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`. Fix any missing `using` or namespace. Run app, open Skills 配置, add a skill, delete it.

```bash
git add SmallEBot/Components/Layout/MainLayout.razor SmallEBot/Components/Skills/SkillsConfigDialog.razor SmallEBot/Components/Skills/AddSkillDialog.razor SmallEBot/Components/Skills/DeleteSkillConfirmDialog.razor
git commit -m "feat(skills): add Skills config dialog, add and delete UI"
```

---

## Task 6: Import skill from folder

**Files:**
- Create: `SmallEBot/Components/Skills/ImportSkillDialog.razor`
- Modify: `SmallEBot/Components/Skills/SkillsConfigDialog.razor`

**Step 1: Implement folder picker via JS and ImportSkillDialog**

Blazor cannot open a native folder picker without JS. Add a simple approach: user pastes or types the source folder path, and optionally the target id. Create `ImportSkillDialog.razor`:

- Two fields: Source folder path (required), Skill id (optional; default from folder name).
- Buttons: Cancel, Import.
- On Import: call `SkillsConfig.ImportUserSkillFromFolderAsync(sourcePath, string.IsNullOrWhiteSpace(id) ? null : id.Trim())`. On success close with Ok; on exception show Snackbar and keep dialog open.

```razor
@inject ISkillsConfigService SkillsConfig
@inject ISnackbar Snackbar

<MudDialog>
    <TitleContent><MudText Typo="Typo.h6">导入 Skill</MudText></TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_sourcePath" Label="源文件夹路径" Variant="Variant.Outlined" Required RequiredError="必填" Class="mb-2" />
        <MudTextField @bind-Value="_id" Label="Skill Id (可选，默认取文件夹名)" Variant="Variant.Outlined" Class="mb-2" />
        <MudText Typo="Typo.caption">源文件夹内必须包含 SKILL.md 且含有效 frontmatter。</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Cancel">取消</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="Import">导入</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;
    private string _sourcePath = "";
    private string _id = "";

    private void Cancel() => MudDialog.Cancel();

    private async Task Import()
    {
        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            Snackbar.Add("请输入源文件夹路径", Severity.Warning);
            return;
        }
        try
        {
            await SkillsConfig.ImportUserSkillFromFolderAsync(_sourcePath.Trim(), string.IsNullOrWhiteSpace(_id) ? null : _id.Trim());
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Snackbar.Add("导入失败: " + ex.Message, Severity.Error);
        }
    }
}
```

**Step 2: Open ImportSkillDialog from SkillsConfigDialog**

In `SkillsConfigDialog.razor`, replace `StartImport` with:

```csharp
private async Task StartImport()
{
    var dialog = await DialogService.ShowAsync<ImportSkillDialog>("导入 Skill", new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
    var result = await dialog.Result;
    if (result?.Canceled != false) return;
    await AgentService.InvalidateAgentAsync();
    Snackbar.Add("已导入", Severity.Success);
    await LoadListAsync();
}
```

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Components/Skills/ImportSkillDialog.razor SmallEBot/Components/Skills/SkillsConfigDialog.razor
git commit -m "feat(skills): add Import skill from folder dialog"
```

---

## Task 7: Ship system skills directory and copy to output

**Files:**
- Create: `SmallEBot/.agents/sys.skills/.gitkeep` (or one sample skill)
- Modify: `SmallEBot/SmallEBot.csproj`

**Step 1: Ensure .agents/sys.skills exists and is copied**

Create directory `SmallEBot/.agents/sys.skills/`. Add a placeholder so the folder is in git (e.g. a sample skill or .gitkeep). Design says "shipped with the app (content copied to output like .sys.mcp.json)". In csproj, add:

```xml
<ItemGroup>
  <Content Include=".agents\sys.skills\**\*" LinkBase=".agents\sys.skills">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

Or if you prefer a single .gitkeep and no content yet:

Create `SmallEBot/.agents/sys.skills/.gitkeep` (empty file). Then in csproj:

```xml
<Content Include=".agents\sys.skills\**\*" LinkBase=".agents\sys.skills" CopyToOutputDirectory="PreserveNewest" />
```

So that any file under sys.skills is copied. If the folder is empty except .gitkeep, the folder will still be created when the first system skill is added to the repo later.

**Step 2: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`. Verify output directory contains `.agents/sys.skills`.

```bash
git add SmallEBot/.agents/sys.skills/.gitkeep SmallEBot/SmallEBot.csproj
git commit -m "chore(skills): add .agents/sys.skills and copy to output"
```

---

## Task 8: Context token count and duplicate-id handling

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Modify: `SmallEBot/Services/SkillsConfigService.cs`

**Step 1: Deduplicate by id (system wins)**

In `SkillsConfigService.AddSkillsFromDirectoryAsync`, when adding a user skill (isSystem: false), skip if `list` already contains an entry with the same `id` (system wins). So before `list.Add(...)` for user skills, add: `if (list.Any(x => x.Id == id)) continue;`.

**Step 2: Include skills block in token count**

In `AgentService`, the context usage uses `AgentInstructions` in `SerializeRequestJsonForTokenCount`. Update so the same instructions string used for the agent (including skills block) is passed to token count. You may need to store the current instructions in a field when building the agent and use that in `GetEstimatedContextUsageAsync` instead of `AgentInstructions` only.

**Step 3: Build and commit**

Run: `dotnet build SmallEBot/SmallEBot.csproj`

```bash
git add SmallEBot/Services/SkillsConfigService.cs SmallEBot/Services/AgentService.cs
git commit -m "fix(skills): deduplicate skill id (system wins), include skills in context token count"
```

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-15-skills-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

2. **Parallel Session (separate)** — Open a new session with executing-plans and run the plan task-by-task with checkpoints.

Which approach do you prefer?
