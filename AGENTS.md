# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Deployment model

**SmallEBot is intended for local deployment.** The app runs on the user’s own machine (e.g. `dotnet run --project SmallEBot` on a dev or personal PC). The Blazor Server host and the agent (including any tools that run commands or access the filesystem) execute on that same machine. For design and tooling, treat “the server” and “the user’s computer” as the same environment.

## English-only UI and logs

**All user-facing text and system output must be in English only.** This includes:
- UI labels, tooltips, buttons, dialog titles, Snackbar messages, and any text rendered in the Blazor app
- Log messages, exception messages shown to users, and any other system output visible in the app or logs
- Default values for entities (e.g. conversation title), error messages, and validation messages

Do not add or leave Chinese (or other non-English) text in the host app (SmallEBot), Application, Infrastructure, or Core. Code comments and git commit messages remain in English per project rules.

## Commands

- **Build** (from repo root): `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj` (solution: `SmallEBot.slnx`)
- **Run** (from repo root): `dotnet run --project SmallEBot`
- **EF Core migrations** (add new migration, from repo root): `dotnet ef migrations add <MigrationName> --project SmallEBot.Infrastructure --startup-project SmallEBot`
  - Migrations live under `SmallEBot.Infrastructure/Data/Migrations`. Pending migrations are applied automatically on startup (see `Program.cs`).
- **Lint**: Use the IDE/linter on `SmallEBot/`; there is no separate lint script. No test project in the repo.

**PowerShell:** Chain commands with `;` (e.g. `cd SmallEBot; dotnet build`), not `&&`. Quote paths with spaces (see `.cursor/rules/powershell-multi-command.mdc`).

## Architecture

- **Projects**: **SmallEBot.Core** (class library) — domain entities, `IConversationRepository`, shared models; no EF or Blazor. **SmallEBot.Application** (class library) — conversation pipeline (`IAgentConversationService`), `IAgentRunner` and `IStreamSink` interfaces; references Core only. Host implements `IAgentRunner` and `IStreamSink`, registers services via Extensions. **SmallEBot.Infrastructure** (class library) — `SmallEBotDbContext`, EF migrations, `ConversationRepository`, BackfillTurns; references Core. **SmallEBot** (Blazor Server host) — UI, SignalR, DI; references Core, Application, and Infrastructure.
- **App**: Blazor Server (`.NET 10`) in `SmallEBot/`. Entry: `Program.cs`; UI: `Components/` (Razor + MudBlazor); Host-only app services in `Services/`, grouped by domain with matching namespaces: **SmallEBot.Services.Agent**, **.Streaming**, **.Conversation**, **.Mcp**, **.Skills**, **.User**, **.Presentation** (see folder names for which types live where). Host service registration is done in `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (e.g. `AddSmallEBotHostServices`). Data access is via **IConversationRepository** (implemented in Infrastructure); the conversation pipeline is **IAgentConversationService** (implemented in Application).
- **Request flow**: Blazor UI (chat page, sidebar, dialogs) → SignalR → scoped services. The chat pipeline is orchestrated by **IAgentConversationService** (create turn, stream to **IStreamSink**, persist). **AgentCacheService** keeps only agent cache invalidation and context % for the UI; **IAgentRunner** (Host’s `AgentRunnerAdapter`) runs the agent and yields stream updates. AgentBuilder composes **IAgentContextFactory** (system prompt + skills block), **IBuiltInToolFactory** (GetCurrentTime, ReadFile, ReadSkill, ExecuteCommand), and **IMcpToolFactory** (MCP tools + clients), then builds/caches the Anthropic agent. **ConversationService** is a thin facade over **IAgentConversationService**. User identity is a single username (first-visit dialog), stored in `UserNameService` (session + `UserPreferencesService`); all conversation data is scoped by that username.
- **Data paths**: Database (`smallebot.db`), preferences (`smallebot-settings.json`), and MCP config (`.agents/`) all use `AppDomain.CurrentDomain.BaseDirectory` — i.e. the app’s run/publish directory. No dependency on `IWebHostEnvironment.ContentRootPath`.
- **MCP**: System MCPs from `.agents/.sys.mcp.json` (content copied to output); user MCPs and disabled-system IDs in `.agents/.mcp.json` via `McpConfigService`. **IMcpToolFactory** loads all enabled MCP servers for the agent; **IMcpToolsLoaderService** remains for the MCP config dialog (single-server tool list for display). After config changes (add/edit/delete/toggle), call `AgentCacheService.InvalidateAgentAsync()` so the next request rebuilds the agent and tools.
- **Terminal**: Built-in **ExecuteCommand** tool runs shell commands on the host (cross-platform). Command blacklist is configured in **Terminal config** (App bar); persisted in `.agents/terminal.json`. Default blacklist blocks common dangerous commands; user can edit in the dialog.
- **Skills**: File-based skills under `.agents/sys.skills/` (system) and `.agents/skills/` (user). Each skill is a folder containing `SKILL.md` with YAML frontmatter (`name`, `description`). `SkillsConfigService` lists and parses metadata; **IAgentContextFactory** injects skill metadata into the system prompt. Built-in tools: **ReadSkill(skillName)** (loads SKILL.md by id from sys.skills or skills), **ReadFile(path)** (allowed extensions under run directory). Skills config UI in `Components/Skills/`. After adding/removing/importing skills, call `AgentCacheService.InvalidateAgentAsync()`.
- **Preferences**: Theme, username, use-thinking mode, and show-tool-calls are persisted in one file (`smallebot-settings.json`) via `UserPreferencesService`; theme is also synced to JS/localStorage for initial paint and `data-theme`. Theming uses `data-theme` on `<html>`, `--seb-*` in `wwwroot/app.css`, and `ThemeProviderHelper` + `ThemeConstants`.
- **Design docs**: `docs/plans/` describe stack, data model, and config (e.g. `ANTHROPIC_API_KEY` or `DeepseekKey` env; `Anthropic`/`DeepSeek` in appsettings). API keys stay in environment or secrets, not in config or source. **README.md** is user-facing (run, features); AGENTS.md and `docs/plans/` are authoritative for architecture and development.
