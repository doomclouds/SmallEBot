# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Commands

- **Build** (from repo root): `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj`
- **Run** (from repo root): `dotnet run --project SmallEBot`
- **EF Core migrations** (add new migration, from repo root): `dotnet ef migrations add <MigrationName> --project SmallEBot`
  - Migrations live under `SmallEBot/Data/Migrations`. Pending migrations are applied automatically on startup (see `Program.cs`).
- **Lint**: Use the IDE/linter on `SmallEBot/`; there is no separate lint script. No test project in the repo.

**PowerShell:** Chain commands with `;` (e.g. `cd SmallEBot; dotnet build`), not `&&`. Quote paths with spaces.

## Architecture

- **App**: Single ASP.NET Core Blazor Server app (`.NET 10`) in `SmallEBot/`. Entry: `Program.cs`; UI: `Components/` (Razor + MudBlazor), data: `Data/` (EF Core + SQLite), app services: `Services/`.
- **Request flow**: Blazor UI (chat page, sidebar, dialogs) → SignalR → scoped services. **AgentService** is the chat module (create turn, stream, complete turn, context %, invalidate); it delegates to **IAgentBuilder** for the actual agent. AgentBuilder composes **IAgentContextFactory** (system prompt + skills block), **IBuiltInToolFactory** (GetCurrentTime, ReadFile, ReadSkill), and **IMcpToolFactory** (MCP tools + clients), then builds/caches the Anthropic agent. `ConversationService` and `ChatMessageStoreAdapter` use `AppDbContext`. User identity is a single username (first-visit dialog), stored in `UserNameService` (session + `UserPreferencesService`); all conversation data is scoped by that username.
- **Data paths**: Database (`smallebot.db`), preferences (`smallebot-settings.json`), and MCP config (`.agents/`) all use `AppDomain.CurrentDomain.BaseDirectory` — i.e. the app’s run/publish directory. No dependency on `IWebHostEnvironment.ContentRootPath`.
- **MCP**: System MCPs from `.agents/.sys.mcp.json` (content copied to output); user MCPs and disabled-system IDs in `.agents/.mcp.json` via `McpConfigService`. **IMcpToolFactory** loads all enabled MCP servers for the agent; **IMcpToolsLoaderService** remains for the MCP config dialog (single-server tool list for display). After config changes (add/edit/delete/toggle), call `AgentService.InvalidateAgentAsync()` so the next request rebuilds the agent and tools.
- **Skills**: File-based skills under `.agents/sys.skills/` (system) and `.agents/skills/` (user). Each skill is a folder containing `SKILL.md` with YAML frontmatter (`name`, `description`). `SkillsConfigService` lists and parses metadata; **IAgentContextFactory** injects skill metadata into the system prompt. Built-in tools: **ReadSkill(skillName)** (loads SKILL.md by id from sys.skills or skills), **ReadFile(path)** (allowed extensions under run directory). Skills config UI in `Components/Skills/`. After adding/removing/importing skills, call `AgentService.InvalidateAgentAsync()`.
- **Preferences**: Theme, username, use-thinking mode, and show-tool-calls are persisted in one file (`smallebot-settings.json`) via `UserPreferencesService`; theme is also synced to JS/localStorage for initial paint and `data-theme`. Theming uses `data-theme` on `<html>`, `--seb-*` in `wwwroot/app.css`, and `ThemeProviderHelper` + `ThemeConstants`.
- **Design docs**: `docs/plans/` (e.g. `2026-02-13-smallebot-phase1-design.md`, `2026-02-15-agent-refactor-design.md`, `2026-02-15-mcp-config-design.md`, `2026-02-15-skills-design.md`) describe stack, data model, and config (e.g. `ANTHROPIC_API_KEY` or `DeepseekKey` env, `Anthropic`/`DeepSeek` in appsettings). API keys stay in environment or secrets, not in config or source.
