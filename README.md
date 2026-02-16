# SmallEBot

A personal chat assistant built with ASP.NET Core Blazor Server, designed to run **on your own machine** (local deployment). The app and the agent run on the same computer you use; there is no separate “server” — the server is your PC. It supports multiple conversations, streaming replies, and optional “thinking” mode via the **Anthropic-compatible** API (e.g. DeepSeek at `api.deepseek.com/anthropic` or native Anthropic).

## Features

- **Multi-conversation**: Create, switch, and delete conversations; history is stored per user.
- **Streaming chat**: Real-time streaming of assistant replies with optional reasoning/tool-call visibility.
- **Thinking mode**: Toggle extended reasoning (e.g. DeepSeek Reasoner) via Anthropic thinking support.
- **Themes**: Several UI themes (e.g. editorial-dark, paper-light, terminal) with persistence.
- **No login**: First visit asks for a username; data is scoped by that name (session + local preferences file).
- **Terminal**: Run shell commands via the agent (ExecuteCommand tool). Command blacklist is configurable in Terminal config (App bar); default blocks common dangerous commands.

## Tech stack

| Layer    | Choice                          |
|----------|----------------------------------|
| Runtime  | .NET 10                          |
| UI       | Blazor Server + MudBlazor        |
| Agent    | Microsoft Agent Framework (Anthropic) |
| LLM      | DeepSeek via Anthropic API, or any Anthropic-compatible endpoint |
| Data     | EF Core + SQLite                 |

## Run locally

**Prerequisites:** .NET 10 SDK.

```bash
# From repo root
dotnet run --project SmallEBot
```

Set the API key via environment variable (do not put it in config or source):

- **Anthropic / DeepSeek:** `ANTHROPIC_API_KEY` or `DeepseekKey`

Example (PowerShell):

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"; dotnet run --project SmallEBot
```

Then open the URL shown in the console (e.g. `https://localhost:5xxx`).

## Configuration

Main options live in `SmallEBot/appsettings.json` under `SmallEBot` and `Anthropic` / `DeepSeek` (e.g. default title, model name, base URL). For DeepSeek use base URL `https://api.deepseek.com/anthropic`. API keys stay in environment or user secrets only.

## Project layout

- **SmallEBot.Core** — Domain entities, repository interfaces, shared models (no EF or UI).
- **SmallEBot.Application** — Conversation pipeline, `IAgentRunner` / `IStreamSink`; host-agnostic.
- **SmallEBot.Infrastructure** — EF Core, SQLite, migrations, repository implementation.
- **SmallEBot/** — Blazor Server host: `Program.cs`, `Components/`, `Services/` (by domain), DI in `Extensions/`.
- **docs/plans/** — Design and implementation notes.

For build, EF migrations, and architecture details, see [AGENTS.md](AGENTS.md).
