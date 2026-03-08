# MCP Configuration UI and File-Based Config — Design

**Status:** Implemented  
**Date:** 2026-02-15  
**Implementation:** See `docs/plans/2026-02-15-mcp-config-implementation-plan.md` (2026-02-15).  
**Target:** Toolbar MCP config button, dialog with guided MCP setup (http/stdio), file-based config under `.agents`, system vs user MCP, enable/disable persistence, and display of tools and prompts per MCP.

---

## 1. Goal and Config Sources

**Goal**  
Add an MCP configuration button in the toolbar that opens a dialog with a guided UI for configuring MCP (http and stdio). The server reads config from the `.agents` directory; the Agent loads only enabled MCPs. System MCPs are read-only in the UI; user-defined MCPs can be added, edited, and removed. Enable/disable state is persisted per MCP.

**Config and storage**

- **Directory:** `.agents` under the application root (`ContentRootPath`), same level as `smallebot-settings.json`.
- **System MCP:** `.agents/.sys.mcp.json` — shipped with the app, read-only. Listed in the UI for display only; not editable or deletable.
- **User MCP:** `.agents/.mcp.json` — written by the app when the user adds/edits/deletes MCPs in the UI; editable and deletable.
- **Enable state:** Add a field to existing `smallebot-settings.json` (e.g. `DisabledMcpIds: string[]`). Any MCP id not in this list is considered enabled. Read/write via `UserPreferencesService`, consistent with theme and other preferences.

**Agent loading**  
When building the tool list, `AgentService`: (1) reads `.sys.mcp.json` and `.mcp.json` and merges them into a full MCP list; (2) reads `DisabledMcpIds` from `UserPreferencesService`; (3) loads only MCPs not in `DisabledMcpIds`. If a given MCP fails to connect, log and skip without blocking others.

---

## 2. JSON Structure and Backend Services

**JSON format (compatible with current appsettings mcpServers)**  
Both files are key → config object. Key is the MCP id (e.g. `microsoft.docs.mcp`).

- **http:** `"type": "http"`, `"url": "https://..."`
- **stdio:** `"type": "stdio"` (optional; if no type but `command` present, treat as stdio), `"command": "npx"`, `"args": ["-y", "package"]`, optional `"env": { "KEY": "value" }`

Example `.sys.mcp.json` / `.mcp.json`:

```json
{
  "microsoft.docs.mcp": { "type": "http", "url": "https://learn.microsoft.com/api/mcp" },
  "my-stdio": { "command": "npx", "args": ["-y", "@some/mcp"] }
}
```

**Backend services**

- **McpConfigService (new):** Resolves `.agents` path; reads `.sys.mcp.json` (read-only) and `.mcp.json` (read + write); merges into a full MCP list with source (system / user); provides save for user MCP (write `.mcp.json`). Singleton or scoped, inject `IWebHostEnvironment`.
- **UserPreferencesService:** Extend `SmallEBotSettings` with `DisabledMcpIds: List<string>` (or equivalent); add get/set; default empty list means all enabled.
- **AgentService:** In `EnsureToolsAsync`, do not read `IConfiguration["mcpServers"]`. Call McpConfigService for merged list, filter by UserPreferencesService `DisabledMcpIds`, then for each enabled entry create transport (http/stdio) and load tools. On per-MCP failure: log and skip.

**Default system MCP and appsettings cleanup**

- **Default system MCP:** The current `appsettings.json` `mcpServers` content (e.g. microsoft.docs.mcp, nuget, context7) is the default system MCP set. Ship this content as `.agents/.sys.mcp.json` in the repo so out-of-the-box behavior matches today; the repo includes this file with the former appsettings entries.
- **Remove MCP from appsettings:** Delete the `mcpServers` node from `appsettings.json` (and `appsettings.Development.json` if present). After this change, MCP config lives only under `.agents` (`.sys.mcp.json` + `.mcp.json`). No fallback to appsettings for MCP. Implementation must switch `AgentService` to load MCP from `McpConfigService` (file-based); until then, removing `mcpServers` means no MCP loads at runtime.

---

## 3. UI and Interaction

**Entry**  
In `MainLayout.razor` AppBar, add an MCP config button (e.g. `Icons.Material.Filled.Settings` or `Extension`) next to “show tool calls” and “thinking mode”, tooltip “MCP 配置”. Click opens a large or fullscreen `MudDialog` for MCP configuration.

**Dialog content**

- **List:** All MCPs (system + user). Each row: name (id), type (http/stdio), short info (URL or command), source (system/user), enable switch (`MudSwitch`). System rows: no edit/delete; user rows: edit and delete. “Add MCP” button above or below the list.
- **Guided add/edit:** “Add MCP” or “Edit” on a user item opens a wizard (or inline form): (1) type = HTTP or Stdio, (2) fields by type (HTTP: name, URL; Stdio: name, command, args, optional env). On save, write user entry to `.mcp.json` via McpConfigService.
- **Enable state:** Toggling the switch updates only `DisabledMcpIds` in UserPreferencesService (on = remove id from list, off = add id). Optionally show a note that changes apply on next Agent build or after reload.

**Technical**  
Dialog component injects McpConfigService and UserPreferencesService to load list and disabled set and to persist. On read/save errors for `.mcp.json`, show Snackbar. Per-MCP connection failures are logged in Agent; list view can optionally show “load failed” for that row in a later iteration.

---

## 4. Display Tools and Prompts per MCP

**Scope**  
MCP exposes both **Tools** and **Prompts**. The config UI shows, for each MCP, its **tools** and **prompts** with names and descriptions (and prompt arguments if available).

**Data source**

- **Tools:** Existing `McpClient.ListToolsAsync()` (name, description).
- **Prompts:** MCP client `ListPromptsAsync()` (or equivalent) for prompt list and description/arguments.
- When the config dialog opens, connect to each **enabled** MCP and call `ListToolsAsync` and `ListPromptsAsync`; cache results in the dialog for display. If a connection fails, show “load failed” or only the MCP name without details. Optional: “Refresh” per MCP to re-fetch tools/prompts.

**List and detail**

- Each MCP row can expand (e.g. `MudExpansionPanel` or nested rows).
- Expanded content:
  - **Tools:** Tool name + short description (from list response).
  - **Prompts:** Prompt name + description; list arguments if present.
- Same presentation for system and user MCPs; only user MCPs have edit/delete.

**Relation to Agent**  
Agent continues to load **tools** from enabled MCPs. If the framework supports registering **prompts** as model capabilities, register them when building the Agent. The config UI only displays tools and prompts; enable/disable remains per-MCP (not per-tool or per-prompt) unless we extend that later.

---

## 5. Error Handling and Edge Cases

- **Missing `.agents` or files:** McpConfigService returns empty list for missing file; create `.agents` when saving user MCP for the first time.
- **Invalid JSON:** Log and return empty or partial list; Snackbar in UI on save failure.
- **Duplicate id in user + system:** Prefer system definition for Agent load; in UI show both or merge with clear “system” vs “user” label (system read-only).
- **DisabledMcpIds contains unknown id:** Ignore when filtering; no need to prune on load.

---

## 6. Summary

| Item | Choice |
|------|--------|
| Config root | `.agents` under ContentRootPath |
| System config | `.agents/.sys.mcp.json` (read-only) |
| User config | `.agents/.mcp.json` (read/write) |
| Enable state | `DisabledMcpIds` in smallebot-settings.json via UserPreferencesService |
| UI | Toolbar button → dialog: list (system + user), enable switch, add/edit wizard (http/stdio) |
| Tools & prompts | Per-MCP expandable section with ListToolsAsync + ListPromptsAsync |
