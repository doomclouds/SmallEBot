# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Commands

- **Build** (from repo root): `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj`
- **Run**: `dotnet run --project SmallEBot`
- **EF Core migrations** (add new migration, from repo root): `dotnet ef migrations add <MigrationName> --project SmallEBot`
  - Migrations live under `SmallEBot/Data/Migrations`. Pending migrations are applied automatically on startup (see `Program.cs`).
- **Lint**: Use the IDE/linter on `SmallEBot/`; there is no separate lint script. No test project in the repo.

## Architecture

- **App**: Single ASP.NET Core Blazor Server app (`.NET 10`) in `SmallEBot/`. Entry: `Program.cs`; UI: `Components/` (Razor + MudBlazor), data: `Data/` (EF Core + SQLite), app services: `Services/`.
- **Request flow**: Blazor UI (chat page, sidebar, dialogs) → SignalR → scoped services. `AgentService` talks to the Microsoft Agent Framework (OpenAI-compatible); `ConversationService` and `ChatMessageStoreAdapter` use `AppDbContext`. User identity is a single username (first-visit dialog), stored in `UserNameService` (session + optional file); all conversation data is scoped by that username.
- **Design docs**: `docs/plans/` (e.g. `2026-02-13-smallebot-phase1-design.md`) describe stack, data model, and config (e.g. `DeepseekKey` env, `SmallEBot`/`DeepSeek` in appsettings). API keys stay in environment or secrets, not in config or source.
