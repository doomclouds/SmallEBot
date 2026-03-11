# Sub-Agent Design

**Date:** 2026-03-11  
**Status:** Approved  
**Approach:** Sub-agent as IToolProvider + streaming to UI

## Problem

Enable the main agent to delegate sub-tasks to specialized sub-agents (e.g. explorer, researcher) that run in parallel with controlled concurrency. Users need to see sub-agent execution in real-time (thinking, tool calls, text) and review results after completion.

## Design Decisions

| Decision | Choice |
|----------|--------|
| Tool API | `RunSubAgent` (returns `Task<string>`), `StopSubAgent` |
| Concurrency | Max 2 concurrent sub-agents; 3rd call waits for slot |
| Session storage | `.agents/conversations/{conversationId}/subAgents/{subAgentId}/session.json` |
| Stream routing | `IAmbientStreamSink` + `SubAgentStreamUpdate` → same Channel as main agent |
| Default sub-agent | Explorer (identity optional; when omitted, use default) |
| System prompt | Add Sub-Agents section |

## Architecture

### Components

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `SubAgentToolProvider` | Infrastructure/Agents/Tools | RunSubAgent, StopSubAgent tools |
| `SubAgentOrchestrator` | Application or Infrastructure | Concurrency (SemaphoreSlim 2), run sub-agent, forward stream |
| `ISubAgentRunner` | Application.Contracts | Run sub-agent with streaming |
| `ISubAgentSessionStore` | Application.Contracts | Load/Save session in subAgents folder |
| `IAmbientStreamSink` | Application.Contracts | AsyncLocal sink for current request |
| `SubAgentStreamUpdate` | Core/Models | Wrapper for sub-agent updates |

### Data Flow

```
Main Agent calls RunSubAgent(identity?, task)
    ↓
SubAgentOrchestrator acquires semaphore (max 2)
    ↓
ISubAgentRunner.RunStreamingAsync → yields StreamUpdate
    ↓
Each update → SubAgentStreamUpdate(subAgentId, name, inner) → IAmbientStreamSink
    ↓
Same Channel as main agent → ChatOrchestrator reads
    ↓
UI: RunSubAgent block rendered as expandable (executing) or normal tool + detail button (completed)
```

### RunSubAgent Parameters

- `identity` (optional): Sub-agent role, responsibilities, scope. When omitted, use default explorer.
- `task` (required): Task description for the sub-agent.

### Default Explorer Sub-Agent

- Identity: "Explore and gather information. Search files, read directories, run safe read-only commands. Report findings concisely."
- Used when `identity` is null or empty.

## UI Design

### Phase 1: Executing (Phase=Started / Executing)

- Render as **expandable block** (MudExpansionPanel or similar)
- Expanded content: sub-agent stream (thinking, tool calls, text) in real-time
- **Max height** with scrollbar (e.g. 400px) when content exceeds
- Collapsed state: "Sub-agent: {name} running..."

### Phase 2: Completed

- Render as **normal ToolCall view** (tool name, args, result summary)
- **Button on the right** (e.g. "View details" / icon)
- Click → **modal/dialog** showing full sub-agent execution (chat-like)
- Modal content: same layout as main chat (thinking, tool calls, text blocks)

### SubAgentStreamUpdate Handling

- `SubAgentStreamUpdate(subAgentId, subAgentName, InnerUpdate)` maps to `ToolCallStreamUpdate(RunSubAgent, CallId=subAgentId)`
- When `ToolCallStreamUpdate(RunSubAgent, Phase=Started)` arrives, create expandable block; sub-agent updates with same `subAgentId` append to that block
- When `Phase=Completed`, store sub-agent updates for modal; show normal tool block + button

## System Prompt

Add section:

```markdown
## Sub-Agents

Tools: `RunSubAgent`, `StopSubAgent`.

Use `RunSubAgent` when a task is self-contained and can be delegated: exploration, research, analysis, or parallel work. Pass `identity` (role, responsibilities) and `task` (what to do). When `identity` is omitted, a default explorer sub-agent is used.

- **Max 2 concurrent:** A third call waits until one completes.
- **StopSubAgent(subAgentId):** Cancel a running sub-agent when you need to abort.
```

## Files

| File | Action |
|------|--------|
| `SmallEBot.Core/Models/StreamUpdate.cs` | Add `SubAgentStreamUpdate` |
| `SmallEBot.Application.Contracts/Agents/Tools/BuiltInToolNames.cs` | Add `RunSubAgent`, `StopSubAgent` |
| `SmallEBot.Application.Contracts/Agents/Execution/IAmbientStreamSink.cs` | New |
| `SmallEBot.Application.Contracts/Agents/SubAgents/ISubAgentRunner.cs` | New |
| `SmallEBot.Application.Contracts/Conversations/Session/ISubAgentSessionStore.cs` | New |
| `SmallEBot.Infrastructure/Agents/Tools/SubAgentToolProvider.cs` | New |
| `SmallEBot.Application/Agents/SubAgents/SubAgentOrchestrator.cs` | New |
| `SmallEBot.Infrastructure/Agents/SubAgents/SubAgentRunner.cs` | New |
| `SmallEBot.Infrastructure/Conversations/Session/SubAgentSessionStore.cs` | New |
| `SmallEBot.Infrastructure/Agents/SubAgents/AmbientStreamSink.cs` | New |
| `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs` | Add `GetSubAgentsSection()` |
| `SmallEBot/Components/Chat/...` | Sub-agent block UI, modal |
