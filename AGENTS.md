# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Deployment model

**SmallEBot runs locally on the user's machine** (e.g. `dotnet run --project SmallEBot`). The Blazor Server host, agent, and tools (filesystem, terminal) all execute on the same PC. Design tooling assuming "server" = "user's computer".

## Language rules

- **UI and logs: English only** — labels, buttons, messages, exception text shown to users
- **Code comments and git commits: English**
- Do not leave Chinese or other non-English text in SmallEBot, Application, Infrastructure, or Core

## Commands

| Task | Command (from repo root) |
|------|--------------------------|
| Build | `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj` |
| Run | `dotnet run --project SmallEBot` |
| EF migration | `dotnet ef migrations add <Name> --project SmallEBot.Infrastructure --startup-project SmallEBot` |

- Solution: `SmallEBot.slnx`
- Migrations auto-apply on startup (`Program.cs` calls `db.Database.Migrate()`)
- No test project; no lint script (use IDE)

**PowerShell:** Use `;` to chain commands, not `&&`. Quote paths with spaces (see `.cursor/rules/powershell-multi-command.mdc`).

## Architecture

### Projects and dependencies

```
SmallEBot.Core          → (no deps) — entities, IConversationRepository, models
SmallEBot.Application   → Core      — IAgentConversationService, IAgentRunner, IStreamSink
SmallEBot.Infrastructure→ Core      — DbContext, ConversationRepository, migrations
SmallEBot (Host)        → Core, Application, Infrastructure — Blazor UI, SignalR, DI
```

### Key files

| Component | Location |
|-----------|----------|
| Entry point | `SmallEBot/Program.cs` |
| DI registration | `SmallEBot/Extensions/ServiceCollectionExtensions.cs` |
| Conversation pipeline | `SmallEBot.Application/Conversation/AgentConversationService.cs` |
| Agent runner | `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` |
| Agent builder | `SmallEBot/Services/Agent/AgentBuilder.cs` |
| System prompt | `SmallEBot/Services/Agent/AgentContextFactory.cs` |
| Built-in tools | `SmallEBot/Services/Agent/Tools/` (IToolProvider, ToolProviderAggregator, *ToolProvider) |
| Allowed file extensions | `SmallEBot.Core/AllowedFileExtensions.cs` — single source for workspace and agent file tools (ReadFile, WriteFile, ReadSkillFile, workspace delete/preview). |
| Workspace (VFS + UI) | `SmallEBot/Services/Workspace/` (IVirtualFileSystem, IWorkspaceService); drawer in `Components/Workspace/`. Drawer: file tree + preview only; delete button not shown for temp/ files; delete allowed only for extensions in `AllowedFileExtensions`; no new file/folder. Polls every 2s when open to refresh tree and open-file preview. |
| Command confirmation | `ICommandConfirmationService` (pending requests); bottom-right strip `Components/Terminal/CommandConfirmationStrip.razor` in MainLayout; context id from Blazor Circuit via `ICurrentCircuitAccessor` and `CircuitContextHandler`. |

Host services are grouped by folder/namespace: **Agent**, **Workspace**, **Mcp**, **Skills**, **Terminal**, **Sandbox**, **Conversation**, **User**, **Presentation**. UI: `Components/` (Razor + MudBlazor); layout and App bar in `Components/Layout/MainLayout.razor`.

### Request flow

```
Blazor UI → SignalR → ConversationService → IAgentConversationService
                                              ↓
                                    CreateTurn + StreamResponse
                                              ↓
                           IAgentRunner (AgentRunnerAdapter) → AIAgent
                                              ↓
                           IStreamSink (ChannelStreamSink) → UI updates
```

**AgentBuilder** composes: `IAgentContextFactory` (system prompt + skills) + `IToolProviderAggregator` + `IMcpToolFactory` → caches `AIAgent`. Single cached agent (reasoner model); thinking on/off per request via ChatOptions.Reasoning in run options.

### Built-in tools

ReadFile, WriteFile, ListFiles, and ExecuteCommand (working directory) are scoped to the **workspace root** (`.agents/vfs/`). ReadSkill is unchanged (reads from `.agents/sys.skills/` and `.agents/skills/`).

| Tool | Purpose |
|------|---------|
| `GetCurrentTime` | Returns current local datetime (machine timezone) |
| `ReadFile(path)` | Read file in workspace (path relative to workspace root) |
| `WriteFile(path, content)` | Write file in workspace |
| `ListFiles(path?)` | List files/dirs in workspace |
| `GrepFiles(pattern, mode?, path?, maxDepth?)` | Search file names by glob (default) or regex pattern; returns JSON with file paths relative to workspace root |
| `GrepContent(pattern, ...)` | Search file content with regex; supports ignoreCase, contextLines, filesOnly, countOnly, invertMatch, filePattern, maxResults; returns JSON with matches |
| `ReadSkill(skillName)` | Load SKILL.md from sys.skills or skills (not workspace) |
| `ReadSkillFile(skillId, relativePath)` | Read a file inside a skill folder; returns JSON `{ "path", "content" }` |
| `ListSkillFiles(skillId, path?)` | List files/dirs inside a skill folder |
| `ExecuteCommand(command)` | Run shell command; working dir defaults to workspace root. Use for Python scripts (e.g. python script.py). Optional user confirmation and whitelist (prefix match) when enabled in Terminal config. |
| `ListTasks` | List current conversation tasks (JSON `{ "tasks": [ { "id", "title", "description", "done" }, ... ] }`) |
| `SetTaskList(tasksJson)` | Create or replace the task list in one call; pass JSON array of `{ "title", "description"? }` objects; returns the created list |
| `CompleteTask(taskId)` | Mark a task as done |
| `ClearTasks` | Delete all tasks for the current conversation; call before SetTaskList when starting a new breakdown |

### Context attachments (@ and /)

In the chat input, typing `@` opens a popover listing workspace files (allowed extensions from `AllowedFileExtensions`). Typing `/` opens a popover listing available skills. Selected items appear in the input as `@path` and `/skillId`. On send:
- **@path** — The file contents are injected into the turn context (per-turn synthetic user message) so the model sees them before the real user message.
- **/skillId** — A directive is injected instructing the model to call `ReadSkill(skillId)` (and related tools) to learn and apply the skill. Full skill content is not injected; the model fetches it via tools. Multiple @ and / per message are supported.
- **Drag-and-drop** — Users can also drag files onto the chat; allowed files are uploaded to workspace temp/, deduplicated by hash (path↔hash index), and appear as @-style chips. Uploads show as loading chips; send is disabled until all complete; closing a chip cancels that upload.

### Configuration

- **API keys**: Config `Anthropic:ApiKey` (e.g. user secrets), or environment `ANTHROPIC_API_KEY` or `DeepseekKey`. Do not commit secrets to source.
- **appsettings.json**: Under `Anthropic` (Anthropic-compatible API): BaseUrl, ApiKey, Model, ContextWindowTokens. Optional `Anthropic:TokenizerPath` for context-usage token counting.
- **Data paths**: All use `AppDomain.CurrentDomain.BaseDirectory` (run directory):
  - `smallebot.db` (SQLite)
  - `smallebot-settings.json` (user preferences)
  - `.agents/vfs/` (workspace — agent file tools and ExecuteCommand cwd)
  - `.agents/.sys.mcp.json` (system MCP)
  - `.agents/.mcp.json` (user MCP + disabled system IDs)
  - `.agents/terminal.json` (command blacklist, require-confirmation flag, confirmation timeout, whitelist). When confirmation is enabled, a bottom-right strip appears for Allow/Reject; approved commands are added to the whitelist (prefix match).
  - `.agents/sys.skills/` and `.agents/skills/` (skill folders)
  - `.agents/tasks/` (per-conversation task list JSON files)

### Cache invalidation

After modifying MCP config or skills, call `AgentCacheService.InvalidateAgentAsync()` to rebuild the agent on next request.

### Theming

Uses `data-theme` attribute on `<html>`, CSS variables `--seb-*` in `wwwroot/app.css`. Theme synced to JS/localStorage for initial paint.

### Design docs

`docs/plans/` contains design and implementation notes. README.md is user-facing; AGENTS.md and docs/plans/ are authoritative for development.
