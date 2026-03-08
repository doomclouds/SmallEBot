# Attachment & Skills Dialog Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace @ and / inline triggers with two buttons that open searchable list dialogs for files and skills.

**Architecture:** Add AddFilesDialog and AddSkillsDialog; simplify InputOrchestrator by removing popover logic; add two icon buttons to ChatInput and EditMessageDialog.

**Tech Stack:** Blazor, MudBlazor, IWorkspaceService, ISkillsConfigService

---

## Task 1: Create AddFilesDialog

**Files:**
- Create: `SmallEBot/Components/Chat/Dialogs/AddFilesDialog.razor`

**Step 1: Create the dialog component**

```razor
@using SmallEBot.Application.Contracts.Workspaces
@inject IWorkspaceService WorkspaceService
@inject IJSRuntime JS

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Add files</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_search" Label="Search" Variant="Variant.Outlined" Immediate="true" Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search" Class="mb-2" />
        <div style="max-height: 300px; overflow-y: auto;">
            @if (_paths.Count == 0)
            {
                <MudText Typo="Typo.body2" Class="text-secondary">No files with allowed extensions.</MudText>
            }
            else
            {
                <MudList T="string" Dense="true" @bind-SelectedValues="_selected" SelectionMode="SelectionMode.Multiple" Style="overflow: visible;">
                    @foreach (var path in FilteredPaths)
                    {
                        <MudListItem T="string" Value="@path">
                            <MudIcon Icon="@Icons.Material.Filled.InsertDriveFile" Size="Size.Small" Class="me-2" />
                            @path
                        </MudListItem>
                    }
                </MudList>
            }
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="@Add" Disabled="@(_selected.Count == 0)">Add</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    private List<string> _paths = [];
    private HashSet<string> _selected = [];
    private string _search = "";

    private IReadOnlyList<string> FilteredPaths => string.IsNullOrWhiteSpace(_search)
        ? _paths
        : _paths.Where(p => p.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

    protected override async Task OnInitializedAsync()
    {
        _paths = (await WorkspaceService.GetAllowedFilePathsAsync()).ToList();
    }

    private void Cancel() => MudDialog.Cancel();

    private void Add() => MudDialog.Close(DialogResult.Ok(_selected.ToList()));
}
```

**Note:** MudList `@bind-SelectedValues` with `HashSet<string>` may need `MudListItem T="string" Value="@path"` and `SelectedValuesChanged` if MudBlazor version differs. If binding fails, use manual selection state with checkboxes.

**Step 2: Verify build**

```powershell
dotnet build
```

---

## Task 2: Create AddSkillsDialog

**Files:**
- Create: `SmallEBot/Components/Chat/Dialogs/AddSkillsDialog.razor`

**Step 1: Create the dialog component**

```razor
@using SmallEBot.Application.Contracts.Agents.Skills
@inject ISkillsConfigService SkillsConfigService

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Add skills</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_search" Label="Search" Variant="Variant.Outlined" Immediate="true" Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search" Class="mb-2" />
        <div style="max-height: 300px; overflow-y: auto;">
            @if (_skills.Count == 0)
            {
                <MudText Typo="Typo.body2" Class="text-secondary">No skills available.</MudText>
            }
            else
            {
                <MudList T="string" Dense="true" @bind-SelectedValues="_selectedIds" SelectionMode="SelectionMode.Multiple" Style="overflow: visible;">
                    @foreach (var skill in FilteredSkills)
                    {
                        <MudListItem T="string" Value="@skill.Id">
                            <MudText Typo="Typo.body2">@skill.Name</MudText>
                            <MudText Typo="Typo.caption" Class="text-secondary ms-1">@skill.Id</MudText>
                        </MudListItem>
                    }
                </MudList>
            }
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="@Add" Disabled="@(_selectedIds.Count == 0)">Add</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    private List<SkillMetadata> _skills = [];
    private HashSet<string> _selectedIds = [];
    private string _search = "";

    private IReadOnlyList<SkillMetadata> FilteredSkills => string.IsNullOrWhiteSpace(_search)
        ? _skills
        : _skills.Where(s => s.Id.Contains(_search, StringComparison.OrdinalIgnoreCase) || (s.Name?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

    protected override async Task OnInitializedAsync()
    {
        _skills = (await SkillsConfigService.GetMetadataForAgentAsync()).ToList();
    }

    private void Cancel() => MudDialog.Cancel();

    private void Add() => MudDialog.Close(DialogResult.Ok(_selectedIds.ToList()));
}
```

**Step 2: Verify build**

```powershell
dotnet build
```

---

## Task 3: Simplify InputOrchestrator

**Files:**
- Modify: `SmallEBot/Components/Chat/Orchestration/InputOrchestrator.cs`

**Step 1: Remove popover-related members**

Remove: `_justSelectedAttachment`, `_inputBeforeSelect`, `_filePaths`, `_skills`, `IsPopoverOpen`, `PopoverKind`, `PopoverFilter`, `FilePaths`, `Skills`, `OnPopoverOpenChanged`, `OnSelectionCompleted`, `HandleInputChangedAsync` (entire method), `SelectAttachment`, `ClosePopover`, `TrimTrailingAtAndSlash`, and all references.

**Step 2: Add AddFiles and AddSkills methods**

```csharp
public void AddFiles(IEnumerable<string> paths)
{
    foreach (var path in paths)
    {
        if (Attachments.OfType<ResolvedPathAttachment>().All(x => x.Path != path))
            Attachments.Add(new ResolvedPathAttachment(path));
    }
    OnStateChanged?.Invoke();
}

public void AddSkills(IEnumerable<string> skillIds)
{
    foreach (var id in skillIds)
    {
        if (!RequestedSkillIds.Any(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase)))
            RequestedSkillIds.Add(id);
    }
    OnStateChanged?.Invoke();
}
```

**Step 3: Simplify HandleInputChangedAsync or replace with direct binding**

If parent uses `HandleInputChangedAsync`, replace with:

```csharp
public void SetInputText(string value)
{
    InputText = value;
    OnStateChanged?.Invoke();
}
```

Or keep `HandleInputChangedAsync` but body is only: `InputText = value; OnStateChanged?.Invoke();`

**Step 4: Remove TrimTrailingAtAndSlash from RemoveAttachment and RemoveSkill**

Since we no longer have @ or / in input, `TrimTrailingAtAndSlash` can be removed. Remove its calls from `RemoveAttachment` and `RemoveSkill`.

**Step 5: Update Reset and InitializeFrom**

Remove any popover-related resets. Keep `InputText`, `Attachments`, `RequestedSkillIds` handling.

---

## Task 4: Update ChatInput

**Files:**
- Modify: `SmallEBot/Components/Chat/Input/ChatInput.razor`
- Delete: `SmallEBot/Components/Chat/Input/InputAttachmentPopover.razor` (after ChatInput no longer references it)

**Step 1: Remove popover and suggestion-key logic**

- Remove `@if (Orchestrator.IsPopoverOpen)` block and `InputAttachmentPopover`
- Remove `_popoverRef`, `_suggestionKeysDotNetRef`, `_suggestionKeysAttached`, `_prevPopoverOpen`
- Remove `Orchestrator.OnSelectionCompleted`, `Orchestrator.OnPopoverOpenChanged`
- Remove `SyncInputAfterSelect`, `OnSuggestionKeyDown`, `HandleKeyDown` (or simplify to not handle Arrow/Enter/Escape for popover)
- Remove `OnAfterRenderAsync` popover/suggestion logic

**Step 2: Add IDialogService and two buttons**

```razor
@inject IDialogService DialogSvc
@inject IWorkspaceService WorkspaceService
@inject ISkillsConfigService SkillsConfigService
```

Above InputAttachmentChips:

```razor
<div class="d-flex gap-1 mb-1">
    <MudIconButton Icon="@Icons.Material.Filled.InsertDriveFile" Size="Size.Small" OnClick="@OpenAddFilesDialog" title="Add files" aria-label="Add files" />
    <MudIconButton Icon="@Icons.Material.Filled.Psychology" Size="Size.Small" OnClick="@OpenAddSkillsDialog" title="Add skills" aria-label="Add skills" />
</div>
```

**Step 3: Add dialog handlers**

```csharp
private async Task OpenAddFilesDialog()
{
    var dialog = await DialogSvc.ShowAsync<AddFilesDialog>("Add files", new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
    var result = await dialog.Result;
    if (result?.Data is List<string> paths)
        Orchestrator.AddFiles(paths);
}

private async Task OpenAddSkillsDialog()
{
    var dialog = await DialogSvc.ShowAsync<AddSkillsDialog>("Add skills", new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
    var result = await dialog.Result;
    if (result?.Data is List<string> skillIds)
        Orchestrator.AddSkills(skillIds);
}
```

Add `@using SmallEBot.Components.Chat.Dialogs` if needed.

**Step 4: Simplify HandleTextChanged**

Ensure it calls `Orchestrator.SetInputText(value)` or `Orchestrator.HandleInputChangedAsync(value)` (if kept minimal).

**Step 5: Remove Dispose popover cleanup**

Remove `Orchestrator.OnPopoverOpenChanged = null` and `JS.InvokeVoidAsync("SmallEBot.setPopoverOpen", false)`.

---

## Task 5: Update EditMessageDialog

**Files:**
- Modify: `SmallEBot/Components/Chat/Dialogs/EditMessageDialog.razor`

**Step 1: Remove InputAttachmentPopover**

EditMessageDialog currently has `InputAttachmentPopover` always rendered. Remove it.

**Step 2: Add two buttons and dialog handlers**

Same pattern as ChatInput: Add files, Add skills buttons; OpenAddFilesDialog, OpenAddSkillsDialog.

**Step 3: Remove popover/suggestion logic**

Remove `_popoverRef`, `_suggestionKeysDotNetRef`, `_suggestionKeysAttached`, `_prevPopoverOpen`, `OnAfterRenderAsync` popover logic, `SyncInputAfterSelect`, `OnSuggestionKeyDown`, `OnPopoverOpenChanged`, `OnSelectionCompleted` subscription, firstRender focus (or keep focus but remove setChatInputValueAndCursorToEnd with closePopover).

**Step 4: Simplify firstRender focus**

Keep `Task.Delay(50)` + focus on first render, but use `setChatInputCursorToEnd` only (no closePopover).

---

## Task 6: Clean up chat.js

**Files:**
- Modify: `SmallEBot/wwwroot/js/chat.js`

**Step 1: Remove suggestion-key and popover JS**

- Remove `isPopoverOpen`, `setPopoverOpen`
- Remove `attachChatInputSuggestionKeys`, `detachChatInputSuggestionKeys`, `_suggestionKeyHandler`, `_suggestionKeyWrapperId`
- Remove `scrollAttachmentPopoverToIndex`
- Simplify `setChatInputValueAndCursorToEnd`: remove `closePopover` parameter and related logic (or remove function if unused)

**Step 2: Simplify send handler**

In `_sendHandler`, remove `window.SmallEBot.isPopoverOpen || _suggestionKeyHandler` check. Enter always sends when not shift.

---

## Task 7: Update InputBar placeholder

**Files:**
- Modify: `SmallEBot/Components/Chat/Input/InputBar.razor`

**Step 1: Change placeholder**

From: `"Plan, @ for context, / for commands"`  
To: `"Plan or ask..."` (or similar)

---

## Task 8: Fix MudList selection binding (if needed)

MudBlazor `MudList` with `SelectionMode.Multiple` and `@bind-SelectedValues` may require `IEqualityComparer` or specific type. If runtime error:

- Use `SelectedValuesChanged` event and manual `HashSet<string>` state
- Or use `MudListItem` with `Selected` and manual multi-select

---

## Task 9: Verify and run

**Step 1: Build**

```powershell
dotnet build
```

**Step 2: Run**

```powershell
dotnet run --project SmallEBot
```

**Step 3: Manual test**

- Click "Add files" → search → select → Add → chips appear
- Click "Add skills" → search → select → Add → chips appear
- Remove chips
- Send message with attachments/skills
- Edit message: same flow

---

## Checklist

- [ ] AddFilesDialog created
- [ ] AddSkillsDialog created
- [ ] InputOrchestrator simplified (AddFiles, AddSkills, no popover)
- [ ] ChatInput: buttons + dialogs, no popover
- [ ] EditMessageDialog: buttons + dialogs, no popover
- [ ] InputAttachmentPopover deleted
- [ ] chat.js cleaned (no suggestion keys, no isPopoverOpen)
- [ ] InputBar placeholder updated
- [ ] Build passes, manual test passes
