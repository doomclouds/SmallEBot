# SmallEBot

A personal chat assistant built with ASP.NET Core Blazor Server, designed to run **on your own machine** (local deployment). The app and the agent run on the same computer you use; there is no separate "server" — the server is your PC. It supports multiple conversations, streaming replies, and optional "thinking" mode via the **Anthropic-compatible** API (e.g. DeepSeek at `api.deepseek.com/anthropic` or native Anthropic).

## Features

- **Multi-conversation**: Create, switch, and delete conversations; history is stored per user.
- **Streaming chat**: Real-time streaming of assistant replies with optional reasoning/tool-call visibility.
- **Thinking mode**: Toggle extended reasoning (e.g. DeepSeek Reasoner) via Anthropic thinking support.
- **MCP tools**: Connect to Model Context Protocol servers for extended capabilities (filesystem, web search, databases, etc.).
- **Skills**: File-based skills system — create custom skills in `.agents/skills/` with YAML frontmatter.
- **Python execution**: Run Python code or scripts via the `RunPython` tool.
- **Terminal**: Execute shell commands via the `ExecuteCommand` tool. Configurable command blacklist.
- **Workspace**: Agent file tools and RunPython/ExecuteCommand use a workspace at `.agents/vfs/`. Open the **Workspace** drawer (App bar) to browse, view, create, delete, and rename files.
- **Themes**: Several UI themes (e.g. editorial-dark, paper-light, terminal) with persistence.
- **No login**: First visit asks for a username; data is scoped by that name.

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

## Built-in Tools

The agent has access to these built-in tools. File paths and working directories for ReadFile, WriteFile, ListFiles, ExecuteCommand, and RunPython are relative to the **workspace** (`.agents/vfs/`). Use the Workspace drawer in the App bar to browse and manage workspace files.

| Tool | Description |
|------|-------------|
| `GetCurrentTime` | Returns current local datetime (machine timezone) |
| `ReadFile(path)` | Read a file in the workspace |
| `WriteFile(path, content)` | Write a file in the workspace |
| `ListFiles(path?)` | List files and subdirectories in the workspace |
| `ReadSkill(skillName)` | Load a skill's `SKILL.md` file (from skills folders, not workspace) |
| `ExecuteCommand(command)` | Run a shell command (cwd defaults to workspace) |
| `RunPython(code?, scriptPath?)` | Execute Python code or a `.py` script in the workspace |

## Skills

Skills are file-based extensions stored in `.agents/sys.skills/` (system) and `.agents/skills/` (user). Each skill is a folder containing a `SKILL.md` file with YAML frontmatter:

```markdown
---
name: my-skill
description: What this skill does
---

# Instructions

Your skill content here...
```

Manage skills via the Skills config dialog in the app bar.

## MCP (Model Context Protocol)

Connect to external MCP servers for extended capabilities. Configuration is stored in `.agents/.mcp.json`. System MCPs are defined in `.agents/.sys.mcp.json`.

Manage MCP servers via the MCP config dialog in the app bar.

## Project layout

- **SmallEBot.Core** — Domain entities, repository interfaces, shared models (no EF or UI).
- **SmallEBot.Application** — Conversation pipeline, `IAgentRunner` / `IStreamSink`; host-agnostic.
- **SmallEBot.Infrastructure** — EF Core, SQLite, migrations, repository implementation.
- **SmallEBot/** — Blazor Server host: `Program.cs`, `Components/`, `Services/` (by domain), DI in `Extensions/`.
- **docs/plans/** — Design and implementation notes.

For build, EF migrations, and architecture details, see [AGENTS.md](AGENTS.md).
