# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Commands

- **Build** (from repo root): `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj`
- **Run**: `dotnet run --project SmallEBot`
- **EF Core migrations** (add new migration, from repo root): `dotnet ef migrations add <MigrationName> --project SmallEBot`
  - Migrations live under `SmallEBot/Data/Migrations`. Pending migrations are applied automatically on startup (see `Program.cs`).
- **Lint**: Use the IDE/linter on `SmallEBot/`; there is no separate lint script. No test project in the repo.

**PowerShell:** Chain commands with `;` (e.g. `cd SmallEBot; dotnet build`), not `&&`. Quote paths with spaces.

## Architecture

- **App**: Single ASP.NET Core Blazor Server app (`.NET 10`) in `SmallEBot/`. Entry: `Program.cs`; UI: `Components/` (Razor + MudBlazor), data: `Data/` (EF Core + SQLite), app services: `Services/`.
- **Request flow**: Blazor UI (chat page, sidebar, dialogs) → SignalR → scoped services. `AgentService` uses the Microsoft Agent Framework (**Anthropic** provider, `Microsoft.Agents.AI.Anthropic`); `ConversationService` and `ChatMessageStoreAdapter` use `AppDbContext`. User identity is a single username (first-visit dialog), stored in `UserNameService` (session + `UserPreferencesService`); all conversation data is scoped by that username.
- **Preferences**: Theme, username, use-thinking mode, and show-tool-calls are persisted in one file (`smallebot-settings.json`) via `UserPreferencesService`; theme is also synced to JS/localStorage for initial paint and `data-theme`. Theming uses `data-theme` on `<html>`, `--seb-*` in `wwwroot/app.css`, and `ThemeProviderHelper` + `ThemeConstants`.
- **Design docs**: `docs/plans/` (e.g. `2026-02-13-smallebot-phase1-design.md`) describe stack, data model, and config (e.g. `ANTHROPIC_API_KEY` or `DeepseekKey` env, `Anthropic`/`DeepSeek` in appsettings). API keys stay in environment or secrets, not in config or source.
