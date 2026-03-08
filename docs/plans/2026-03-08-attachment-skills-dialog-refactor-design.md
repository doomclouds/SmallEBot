# Attachment & Skills Dialog Refactor Design

**Date:** 2026-03-08  
**Status:** Approved

## Summary

Replace the @ and / inline trigger mode with two separate buttons that open searchable list dialogs for files and skills. Simplify InputOrchestrator by removing all popover-related logic.

## Goals

- Remove @ and / input-trigger complexity (popover, keyboard handling, DOM sync)
- Add "Add files" and "Add skills" buttons that open searchable dialogs
- Keep chip display and removal unchanged
- Preserve drag-and-drop upload

## Architecture

### Component Structure

| Component | Role |
|-----------|------|
| **ChatInput** | Adds two icon buttons above InputBar; keeps InputAttachmentChips, drop zone |
| **AddFilesDialog** | MudDialog with search field + scrollable file list; multi-select; Add button |
| **AddSkillsDialog** | MudDialog with search field + scrollable skill list; multi-select; Add button |
| **InputOrchestrator** | Simplified: Attachments, RequestedSkillIds, Add/Remove, Collect; no popover state |

### Data Flow

1. User clicks "Add files" → `DialogService.ShowAsync<AddFilesDialog>` → user searches, selects, confirms → dialog returns `List<string>` paths → Orchestrator adds `ResolvedPathAttachment` for each
2. User clicks "Add skills" → `DialogService.ShowAsync<AddSkillsDialog>` → user searches, selects, confirms → dialog returns `List<string>` skillIds → Orchestrator adds to RequestedSkillIds
3. Chip removal unchanged: `RemoveAttachment` / `RemoveSkill`

### To Remove

- `InputAttachmentPopover.razor`
- `InputOrchestrator`: `HandleInputChangedAsync` @/ detection, `IsPopoverOpen`, `PopoverKind`, `PopoverFilter`, `OnPopoverOpenChanged`, `OnSelectionCompleted`, `SelectAttachment`, `ClosePopover`, `TrimTrailingAtAndSlash`, `_justSelectedAttachment`, `_inputBeforeSelect`, `_filePaths`, `_skills`
- `chat.js`: `attachChatInputSuggestionKeys`, `detachChatInputSuggestionKeys`, `isPopoverOpen`, `setPopoverOpen`, `scrollAttachmentPopoverToIndex`, `setChatInputValueAndCursorToEnd` (closePopover param)
- ChatInput / EditMessageDialog: popover rendering, `OnPopoverOpenChanged`, `OnSelectionCompleted`, `SyncInputAfterSelect`, `HandleKeyDown` for suggestion keys, `_suggestionKeysDotNetRef`, `_suggestionKeysAttached`, `_prevPopoverOpen`
- InputBar placeholder: remove "Plan, @ for context, / for commands" → e.g. "Plan or ask..."

## UI Layout

### ChatInput Area (before InputBar)

```
[Add files] [Add skills]  ← icon buttons, compact
<InputAttachmentChips />
<InputBar />
```

- Buttons: `MudIconButton` with `InsertDriveFile` and `Psychology` (or similar) icons
- Tooltip: "Add files" / "Add skills"

### AddFilesDialog

- Title: "Add files"
- Content: `MudTextField` (search, Immediate), `MudList` with `MudListItem` (file path + icon)
- Filter: `path.Contains(filter, OrdinalIgnoreCase)`
- Selection: `MudList` with `SelectionMode="Multiple"` or manual multi-select via checkboxes
- Actions: Cancel, Add

### AddSkillsDialog

- Title: "Add skills"
- Content: `MudTextField` (search), `MudList` with `MudListItem` (skill name + id)
- Filter: `skill.Id.Contains(filter) || skill.Name.Contains(filter)`
- Selection: same as files
- Actions: Cancel, Add

### EditMessageDialog

- Same two buttons + chips layout
- Uses same Orchestrator; dialogs receive Orchestrator as parameter or callback to add items

## InputOrchestrator Simplified API

```csharp
// Input
public string InputText { get; set; }
public List<AttachmentItem> Attachments { get; }
public List<string> RequestedSkillIds { get; }

// Add (called from dialogs)
public void AddFiles(IEnumerable<string> paths);
public void AddSkills(IEnumerable<string> skillIds);

// Remove (unchanged)
public void RemoveAttachment(AttachmentItem item);
public void RemoveSkill(string skillId);

// Upload (unchanged)
public void AddPendingUpload(string uploadId, string fileName);
public void ReportUploadProgress(string uploadId, int progress);
public void OnUploadComplete(string uploadId, string path, string? replacedOldPath);

// Collect
public (string Text, List<string> AttachedPaths, List<string> RequestedSkillIds) Collect();

// Init
public void InitializeFrom(string text, IEnumerable<AttachmentItem> attachments, IEnumerable<string> skillIds);
public void Reset();
```

- `HandleInputChangedAsync` becomes `InputText = value; OnStateChanged?.Invoke();` (no @/ logic)
- Or: remove `HandleInputChangedAsync`; parent binds `InputText` directly and calls `OnStateChanged` when needed

## Dependencies

- `AddFilesDialog`: `IWorkspaceService` (GetAllowedFilePathsAsync)
- `AddSkillsDialog`: `ISkillsConfigService` (GetMetadataForAgentAsync)
- Both: `IDialogService` (parent opens via ShowAsync)

## Error Handling

- Empty workspace: show "No files with allowed extensions" in AddFilesDialog
- No skills: show "No skills available" in AddSkillsDialog
- Duplicate add: `AddFiles` / `AddSkills` skip already-present items (idempotent)
