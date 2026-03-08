# Automatic Context Compression Design

**Goal:** Implement automatic context compression when usage reaches 80%, with manual `/compact` command support.

**Architecture:** Store compressed context summary in database, inject into system prompt. Messages before compression are excluded from LLM history and token calculation but still visible in UI.

**Tech Stack:** C# 14, .NET 10, EF Core, LLM-based summarization

---

## Overview

When context usage reaches 80%, automatically compress older conversation history into a concise summary. The summary is stored in the database and injected into the system prompt, while original messages remain visible in UI but are excluded from LLM context.

## Triggers

1. **Automatic**: When context usage ≥ 80% before processing a new message
2. **Manual**: User invokes `/compact` command

## Data Model

### Database Changes

Add to `Conversations` table:
- `CompressedContext` (TEXT NULL) - The compressed summary content
- `CompressedAt` (TEXT NULL) - ISO datetime when compression occurred

### Message Filtering

- **UI Display**: All messages (including before compression)
- **LLM History**: Only messages WHERE `CreatedAt > CompressedAt`
- **Token Calculation**: `CompressedContext` + messages after `CompressedAt`

## Components

### 1. Compact Skill (`.agents/vfs/sys.skills/compact/SKILL.md`)

Minimal skill containing only the compression workflow prompt for LLM:

```yaml
---
name: compact
description: Compress conversation history into a concise summary. Use when context is running low or user requests /compact.
---

# Context Compression

You are compressing conversation history to save context space.

## Input
You will receive conversation messages (user + assistant + tool calls).

## Task
Generate a structured summary preserving:

1. **Key Decisions**: Important choices made and why
2. **Files Modified**: Paths and what changed (briefly)
3. **Current State**: What's been accomplished, what's pending
4. **Important Context**: Names, values, configurations that matter

## Format
Use this compact format:

```
## Summary
[1-2 sentences overview]

## Decisions
- [decision]: [reasoning]

## Files
- path/to/file: [change summary]

## State
- Done: [items]
- Pending: [items]

## Context
- [key=value pairs or important notes]
```

Keep total output under 500 tokens. Focus on what's needed to continue the work.
```

### 2. Built-in Tool: `CompactContext()`

Triggered by:
- Auto-detection when context ≥ 80%
- Manual `/compact` command

Workflow:
1. Load messages before `CompressedAt` (or all if first compression)
2. Call LLM with compact skill prompt + old messages
3. Store result in `CompressedContext`
4. Update `CompressedAt` to current time
5. Return success/failure

### 3. AgentRunnerAdapter Changes

Before running agent:
```csharp
var usage = await GetContextUsage(conversationId);
if (usage >= 0.8)
{
    await CompactContext(conversationId);
}
```

### 4. AgentContextFactory Changes

Add compressed context section to system prompt:
```csharp
if (!string.IsNullOrEmpty(conversation.CompressedContext))
{
    sections.Add($"# Conversation Summary\n\n{conversation.CompressedContext}");
}
```

### 5. AgentCacheService Changes

Token estimation must include:
1. Compressed context tokens
2. Only messages after `CompressedAt`

## Data Flow

**Before compression:**
```
System Prompt: [base instructions + skills]
History: [msg1, msg2, ..., msgN] (all messages)
Token calc: sum of all message tokens
```

**After compression:**
```
System Prompt: [base instructions + skills] + [Compressed Context Summary]
History: [msg_K, ..., msgN] (only messages after CompressedAt)
Token calc: compressed_context_tokens + post_compression_message_tokens
```

## Files to Change

| File | Change |
|------|--------|
| `Core/Entities/Conversation.cs` | Add `CompressedContext` and `CompressedAt` properties |
| `Infrastructure/Data/Migrations/` | New migration for columns |
| `Services/Agent/Tools/BuiltInToolNames.cs` | Add `CompactContext` |
| `Services/Agent/Tools/ConversationToolProvider.cs` | Implement `CompactContext` tool |
| `Services/Agent/AgentRunnerAdapter.cs` | Auto-trigger on 80% |
| `Services/Agent/AgentContextFactory.cs` | Inject compressed context |
| `Services/Agent/AgentCacheService.cs` | Update token estimation |
| `Core/Repositories/IConversationRepository.cs` | Add method to update compression fields |
| `Infrastructure/Data/ConversationRepository.cs` | Implement update method |
| `.agents/vfs/sys.skills/compact/SKILL.md` | Create skill file |

## UI Interaction

During compression, the UI must:
1. **Show compression indicator**: Display "Compressing context..." with a spinner
2. **Disable input**: Block new message input until compression completes
3. **Show completion**: Hide indicator and re-enable input after compression

### Events

- `CompressionStarted(conversationId)`: Fired when compression begins
- `CompressionCompleted(conversationId, success)`: Fired when compression ends

### Components

- `StreamingIndicator`: Extended with `IsCompressing` and `CompressionMessage` parameters
- `ChatInputArea`: Uses `IsStreaming || IsCompressing` to disable input
- `ChatArea`: Subscribes to compression events from `IAgentConversationService`

## Edge Cases

1. **First compression**: `CompressedAt` is null, compress all messages
2. **Re-compression**: `CompressedAt` exists, only compress messages between old `CompressedAt` and now, append to existing summary
3. **Empty conversation**: Skip compression, no-op
4. **Compression failure**: Log error, continue without compression (don't block user)

## Configuration

Optional future: Add to `.agents/agent.json`:
```json
{
  "CompressionThreshold": 0.8,
  "CompressionEnabled": true
}
```

For now: Hardcode 80% threshold.
