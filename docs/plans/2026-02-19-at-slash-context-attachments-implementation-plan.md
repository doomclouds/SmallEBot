# @ and / Context Attachments — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Let the user type `@` or `/` in the chat input to attach workspace files or request skills; selected items appear as `@path` and `/skillId`; on send, file contents are injected into the turn context and skills are hinted so the model calls ReadSkill etc.

**Architecture:** Backend extends the conversation pipeline and runner with optional `attachedPaths` and `requestedSkillIds`. A per-turn context fragment is built (file contents via IWorkspaceService + skill-hint lines), then injected by prepending a synthetic User message to the message list for that run only. Front-end detects @ and /, shows popovers (flat list of allowed workspace files, flat list of skills), inserts tokens on selection, and on send parses the input to produce the two lists and passes them to the pipeline.

**Tech Stack:** Blazor Server, MudBlazor, .NET 10, SmallEBot.Application (IAgentConversationService, IAgentRunner), Host (AgentRunnerAdapter, IWorkspaceService, ISkillsConfigService). Design: `docs/plans/2026-02-19-at-slash-context-attachments-design.md`.

**Reference:** AGENTS.md (allowed extensions, workspace, skills, conversation flow). No test project; use `dotnet build` and manual verification.

---

## Task 1: Add turn-context fragment builder (Host)

**Files:**
- Create: `SmallEBot/Services/Conversation/ITurnContextFragmentBuilder.cs`
- Create: `SmallEBot/Services/Conversation/TurnContextFragmentBuilder.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (register the new service)

**Step 1: Define interface**

In `ITurnContextFragmentBuilder.cs`:

```csharp
namespace SmallEBot.Services.Conversation;

/// <summary>Builds the per-turn context fragment (attached file contents + requested skill hints) for injection into the agent run.</summary>
public interface ITurnContextFragmentBuilder
{
    /// <summary>Returns a fragment string to prepend to the user message for this turn, or null/empty if nothing to add.</summary>
    Task<string?> BuildFragmentAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default);
}
```

**Step 2: Implement builder**

In `TurnContextFragmentBuilder.cs`:

- Inject `IWorkspaceService` and `ISkillsConfigService`.
- For each path in `attachedPaths`: if `AllowedFileExtensions.IsAllowed(Path.GetExtension(path))`, call `IWorkspaceService.ReadFileContentAsync(path)`. If content is null, append a line "File {path} could not be loaded." Else append "--- {path} ---\n{content}\n\n". De-duplicate paths (e.g. use a HashSet).
- For each id in `requestedSkillIds`: append a line: "The user wants you to use the skill \"{id}\". Call ReadSkill(\"{id}\") (and ReadSkillFile / ListSkillFiles as needed) to learn and apply it." De-duplicate ids.
- If both blocks are empty, return null. Otherwise return "Attached context for this turn:\n\n" + files block + (skills block if any) + "\n--- User message below ---\n\n".
- Use `SmallEBot.Core.AllowedFileExtensions` and `SmallEBot.Services.Workspace.IWorkspaceService`, `SmallEBot.Services.Skills.ISkillsConfigService`. Optional: validate skill id exists via metadata; if not, still add "The user requested skill \"{id}\"; it was not found in the skills list."

**Step 3: Register in DI**

In `ServiceCollectionExtensions.cs`, register `ITurnContextFragmentBuilder` -> `TurnContextFragmentBuilder` (scoped).

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Conversation/ITurnContextFragmentBuilder.cs SmallEBot/Services/Conversation/TurnContextFragmentBuilder.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(conversation): add turn context fragment builder for @ and / attachments"
```

---

## Task 2: Extend IAgentRunner and AgentRunnerAdapter with attachment parameters

**Files:**
- Modify: `SmallEBot.Application/Streaming/IAgentRunner.cs`
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add optional parameters to IAgentRunner.RunStreamingAsync**

In `IAgentRunner.cs`, change the method signature to:

```csharp
IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
    Guid conversationId,
    string userMessage,
    bool useThinking,
    CancellationToken cancellationToken = default,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null);
```

**Step 2: Implement in AgentRunnerAdapter**

In `AgentRunnerAdapter.cs`:

- Inject `ITurnContextFragmentBuilder` in the constructor.
- In `RunStreamingAsync`, after building `frameworkMessages` (history + user message), if `attachedPaths` or `requestedSkillIds` is non-empty (e.g. `(attachedPaths?.Count ?? 0) + (requestedSkillIds?.Count ?? 0) > 0`), call `await _fragmentBuilder.BuildFragmentAsync(attachedPaths ?? [], requestedSkillIds ?? [], cancellationToken)`. If the result is non-null and non-empty, **replace** the last message (the current user message) with two messages: first a User message with content = fragment + "\n\n" + userMessage (so the synthetic block and real user message are in one User message), OR prepend a User message with content = fragment and keep the next message as userMessage. Design says: prepend a single User message containing fragment, then the real user message. So: insert at index `frameworkMessages.Count` (before the last user message) a new ChatMessage(ChatRole.User, fragment), then the last message stays as the real user message. So build: [history..., new User(fragment), new User(userMessage)].
- If fragment is null or empty, keep current behaviour (no extra message).

**Step 3: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds. Fix any call sites of `RunStreamingAsync` (they can pass null/omit the new parameters).

**Step 4: Commit**

```bash
git add SmallEBot.Application/Streaming/IAgentRunner.cs SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): pass attached paths and skill ids into runner for turn context"
```

---

## Task 3: Extend IAgentConversationService and AgentConversationService

**Files:**
- Modify: `SmallEBot.Application/Conversation/IAgentConversationService.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Add optional parameters to CreateTurnAndUserMessageAsync**

In `IAgentConversationService.cs`, add to the method:

```csharp
Task<Guid> CreateTurnAndUserMessageAsync(
    Guid conversationId,
    string userName,
    string userMessage,
    bool useThinking,
    CancellationToken cancellationToken = default,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null);
```

**Step 2: Add optional parameters to StreamResponseAndCompleteAsync**

In `IAgentConversationService.cs`, add:

```csharp
Task StreamResponseAndCompleteAsync(
    Guid conversationId,
    Guid turnId,
    string userMessage,
    bool useThinking,
    IStreamSink sink,
    CancellationToken cancellationToken = default,
    string? commandConfirmationContextId = null,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null);
```

**Step 3: Implement in AgentConversationService**

In `AgentConversationService.cs`:

- Add the new parameters to both methods. For `CreateTurnAndUserMessageAsync`, ignore the new parameters (they are not persisted; only userMessage is stored). For `StreamResponseAndCompleteAsync`, pass `attachedPaths` and `requestedSkillIds` through to `agentRunner.RunStreamingAsync(...)`.

**Step 4: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds. Call sites (ChatArea) do not need to change yet (optional args).

**Step 5: Commit**

```bash
git add SmallEBot.Application/Conversation/IAgentConversationService.cs SmallEBot.Application/Conversation/AgentConversationService.cs
git commit -m "feat(conversation): add attached paths and skill ids to pipeline API"
```

---

## Task 4: Add parser for @ and / tokens from input (Host or shared)

**Files:**
- Create: `SmallEBot/Services/Conversation/AttachmentInputParser.cs` (static or small service)

**Step 1: Implement parser**

- Static class or static methods: `ParseAttachedPaths(string input)` returns `IReadOnlyList<string>`, `ParseRequestedSkillIds(string input)` returns `IReadOnlyList<string>`.
- Rule for paths: match `@` followed by non-whitespace that does not include newline; regex e.g. `@([^\s@/]+)` to capture path (no spaces). So `@docs/readme.md` -> "docs/readme.md". De-duplicate and return.
- Rule for skills: match `/` at start of input or after whitespace, then skill id (alphanumeric, underscore, hyphen). Regex e.g. `/([a-zA-Z0-9_-]+)`. De-duplicate and return.
- Put in namespace `SmallEBot.Services.Conversation` or `SmallEBot.Models`; no DI needed if static.

**Step 2: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Conversation/AttachmentInputParser.cs
git commit -m "feat(chat): add parser for @path and /skillId from input text"
```

---

## Task 5: Wire ChatArea to parse and pass attachment lists

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (or .razor.cs if split)

**Step 1: Parse on send and pass to pipeline**

In `Send()` (or equivalent), after `var msg = _input.Trim();`:

- Call `AttachmentInputParser.ParseAttachedPaths(msg)` and `AttachmentInputParser.ParseRequestedSkillIds(msg)` to get the two lists.
- Pass them to `ConversationPipeline.CreateTurnAndUserMessageAsync(..., msg, UseThinkingMode, _sendCts!.Token, attachedPaths, requestedSkillIds)`.
- Pass the same lists to `StreamResponseAndCompleteAsync(..., attachedPaths: attachedPaths, requestedSkillIds: requestedSkillIds)`.

**Step 2: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds. Manual test: send a message with no @ or /; behaviour unchanged. (With backend only, UI still does not show popovers; user can type @path or /id by hand to test backend.)

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(chat): pass parsed @ and / lists to conversation pipeline"
```

---

## Task 6: Expose workspace file list for @ popover (API for UI)

**Files:**
- Modify: `SmallEBot/Services/Workspace/IWorkspaceService.cs` (optional: add method)
- Modify: `SmallEBot/Services/Workspace/WorkspaceService.cs` (implement flatten + filter)

**Step 1: Add method to get flat list of allowed files**

Option A: Add to `IWorkspaceService`: `Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default);` returning relative paths of all files under the workspace whose extension is in `AllowedFileExtensions.Set`.

Option B: Keep using `GetTreeAsync()` and flatten in the UI. Design says "flatten the tree to a list of nodes where !IsDirectory and AllowedFileExtensions.IsAllowed(...)". So the UI can call `GetTreeAsync()` and flatten. No backend change required; document in plan that UI flattens.

Choose Option B for fewer changes: no new API. Document that the Blazor component will call `GetTreeAsync()` and flatten to a list of `WorkspaceNode` where `!node.IsDirectory` and `AllowedFileExtensions.IsAllowed(Path.GetExtension(node.RelativePath))`.

**Step 2: (If Option A) Implement in WorkspaceService**

Walk the tree, collect all nodes with `!IsDirectory` and allowed extension, return list of `RelativePath`.

**Step 3: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds. (If Option B only, no code change in this task; skip to Task 7 and have the popover use GetTreeAsync + flatten in component.)

**Recommendation:** Implement Option A so the UI has a single call. Add `GetAllowedFilePathsAsync` to `IWorkspaceService` and `WorkspaceService`: in `WorkspaceService`, use existing `WalkDirectory` logic, collect file paths where extension is allowed, return them.

**Step 4: Commit**

```bash
git add SmallEBot/Services/Workspace/IWorkspaceService.cs SmallEBot/Services/Workspace/WorkspaceService.cs
git commit -m "feat(workspace): add GetAllowedFilePathsAsync for @ popover"
```

---

## Task 7: Add @ and / popover UI component(s)

**Files:**
- Create: `SmallEBot/Components/Chat/AttachmentPopover.razor` (or two: `FileListPopover.razor`, `SkillListPopover.razor`)
- Modify: `SmallEBot/Components/Chat/ChatInputBar.razor` (or ChatArea) to show popover on @ or /

**Step 1: Create popover for files**

- Component receives: `bool Open`, `IReadOnlyList<string> FilePaths` (or paths from parent), `EventCallback<string> OnSelect`, `EventCallback OnClose`.
- When Open is true, render a MudMenu or MudPopover anchored below the input (or use MudAutocomplete pattern). List items: each path; on click call OnSelect(path) and OnClose.
- Optional: filter text field; filter list by path containing the text.

**Step 2: Create popover for skills**

- Component receives: `bool Open`, `IReadOnlyList<SkillMetadata>` or `IReadOnlyList<(string Id, string Name)>`, `EventCallback<string> OnSelect` (skillId), `EventCallback OnClose`.
- List items: show Id and Name; on click call OnSelect(skillId), OnClose.

**Step 3: Trigger from input**

- ChatInputBar currently uses MudTextField with Value and ValueChanged. To detect @ or /, we need either (a) JS interop to keyup and open a popover from the parent, or (b) a wrapper that handles keyup and shows popover. Prefer: parent (ChatArea) holds state for "popover kind" (none / file / skill) and "popover open"; when input value changes, if the last typed char is @ or /, set popover open and kind. But MudTextField does not expose per-key events easily from C#. Alternative: add a small JS that fires a custom event or calls DotNet when user types @ or /; the Blazor component subscribes and opens the popover. Or: use MudTextField's Immediate="true" and on ValueChanged check if the new value ends with @ or has @ or / at the right place (e.g. after @ the popover opens when we have @). Simple approach: on ValueChanged, if the new value ends with '@' or ends with '/' (and previous did not), set a flag to open file or skill popover; parent renders the popover and passes file list / skill list. Popover anchor: render the popover inside the ChatArea or ChatInputBar, anchored to the input bar div.
- Implement: In ChatArea (or ChatInputBar), add `_showFilePopover`, `_showSkillPopover`. In the input's ValueChanged, detect if user just typed @ (e.g. value ends with @ and length increased by 1) or / (value ends with / and length increased by 1); set _showFilePopover or _showSkillPopover to true. Render AttachmentPopover (or two popovers) with Open = _showFilePopover or _showSkillPopover, and when Open pass file paths from IWorkspaceService.GetAllowedFilePathsAsync() or from GetTreeAsync flattened (if Task 6 Option B), and skills from ISkillsConfigService.GetMetadataForAgentAsync() or GetAllAsync(). On select: insert the path or skillId into _input (e.g. append @path or /skillId), close popover, StateHasChanged. On close without select: set _showFilePopover/_showSkillPopover to false.
- Insertion: currently _input is bound to ChatInputBar. So the parent (ChatArea) owns _input. When user selects a file, we need to insert "@" + path (or path if @ already in input) at caret. If we only append, we can do _input += "@" + path (but then we have @@path). So when we open on @, the input already has "@"; on select we should replace the trailing "@" with "@path" or append "path" so result is "@path". So _input = _input.TrimEnd('@') + "@" + path (or _input + path if we opened on @ so _input ends with @). Same for /: _input ends with "/"; on select _input = _input.TrimEnd('/') + "/" + skillId.
- Load data: when opening file popover, call GetAllowedFilePathsAsync (or flatten GetTreeAsync) once; when opening skill popover, call GetAllAsync or GetMetadataForAgentAsync once. Use @inject IWorkspaceService and ISkillsConfigService in the component that owns the popover.

**Step 4: Build and verify**

Run: `dotnet build`. Run app: open chat, type @, see popover with files; select one, see @path in input. Type /, see skills; select one, see /skillId.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/AttachmentPopover.razor SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(chat): add @ and / popovers for workspace files and skills"
```

---

## Task 8: (Optional) AGENTS.md and README

**Files:**
- Modify: `AGENTS.md`
- Modify: `README.md` (if user-facing)

**Step 1: Document @ and / in AGENTS.md**

Under a "Chat input" or "Context attachments" subsection: describe that typing @ opens a list of workspace files (allowed extensions), typing / opens skills; selected items appear as @path and /skillId; on send, file contents are added to the turn context and skills are hinted for the model to use via ReadSkill. Reference AllowedFileExtensions.

**Step 2: Commit**

```bash
git add AGENTS.md
git commit -m "docs: describe @ and / context attachments in AGENTS.md"
```

---

## Execution summary

| Task | Description |
|------|-------------|
| 1 | Turn context fragment builder (ITurnContextFragmentBuilder + impl + DI) |
| 2 | IAgentRunner + AgentRunnerAdapter: attachedPaths, requestedSkillIds, inject fragment |
| 3 | IAgentConversationService + AgentConversationService: add params, pass through |
| 4 | AttachmentInputParser for @path and /skillId |
| 5 | ChatArea: parse input and pass lists to pipeline |
| 6 | IWorkspaceService.GetAllowedFilePathsAsync (or document flatten in UI) |
| 7 | Popover UI for @ and /, trigger from input, insert on select |
| 8 | AGENTS.md (and optional README) |

After Task 5, backend is complete (user can type @path and /id by hand and get context). After Task 7, full UX works. Task 8 is optional.

---

## Verification before completion

- Build: `dotnet build` succeeds.
- Run: `dotnet run --project SmallEBot`. Create conversation, type @, choose a file, send; reply should reflect file content. Type /, choose a skill, send; model should be prompted to use that skill.
- No secrets or build artifacts committed.
