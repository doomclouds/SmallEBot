# Session-Centric Architecture + CLI-Style UI

**Date**: 2026-03-09  
**Status**: Approved  
**Approach**: One-shot refactor (data layer + UI layer simultaneously)

## Problem

The current Turn-based conversation model introduces unnecessary complexity:

- `ConversationMetadata` maintains a `TurnInfo` list with `firstMessageIndex` pointers into `AgentSession`
- UI reconstruction requires merging Metadata + Session via `GetChatBubblesAsync`
- Truncation, editing, and compression must coordinate two data sources
- Bubble-style rendering (`MudChat`) adds visual overhead without UX benefit

## Design Decisions

| Decision | Choice |
|----------|--------|
| Data source | AgentSession is the single source of truth |
| Metadata | Slim: Title, UserName, timestamps, CompressedContext only — no Turns |
| UI style | Full CLI: linear flow, role prefixes, collapsible tool calls, no bubbles |
| Restart | "Restart from here" button on user messages; truncates session and re-runs |
| Edit | Truncate to message index + replace content + re-run |
| Interruption | Save session as-is (including incomplete assistant replies) |
| Compression | Kept; operates directly on session message list; summary stored in metadata |
| Title generation | Auto-generated on first message, stored in metadata |
| Attachments/Skills | Encoded as text in the user message by the UI layer before sending |

## Data Layer

### ConversationMetadata (simplified)

```csharp
public class ConversationMetadata : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; }
    public string? Title { get; private set; }
    public string UserName { get; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? CompressedContext { get; private set; }
    public DateTime? CompressedAt { get; private set; }
}
```

**Removed**: `TurnInfo`, `Turns` property, all Turn-related methods (`AddTurn`, `RemoveTurn`, `GetTurn`, `SetFirstMessageIndex`, `RemoveTurnsAfter`, `RemoveTurnAndSubsequent`, `RemoveTurnsBeforeCompression`).

### IAgentSessionStore (simplified)

| Method | Status |
|--------|--------|
| `LoadAsync` | Keep |
| `SaveAsync` | Keep |
| `DeleteAsync` | Keep |
| `TruncateFromIndexAsync(conversationId, messageIndex, agent)` | Replace `TruncateFromTurnAsync` |
| `TruncateBeforeIndexAsync` | Keep (compression) |
| `GetSessionJsonAsync` | Keep |
| `RemoveLastMessageIfAssistantApprovalRequestAsync` | Remove |

### IAgentSessionReader (simplified)

| Method | Status |
|--------|--------|
| `GetMessagesAsync` | Keep |
| `GetUserMessageIndicesAsync` | New — returns indices of all user messages |
| `GetUserMessageContentAsync` | Remove |
| `GetOrphanedApprovalRequestsAsync` | Remove |

### IConversationSessionCoordinator

**Abolished entirely.** Responsibilities absorbed by `ConversationService` and `AgentRunner`.

## Service Layer

### ConversationService

| Old Method | New Method | Change |
|-----------|------------|--------|
| `CreateTurnAndUserMessageAsync` | `SendMessageAsync` | Add user message to session; generate title on first message; save metadata. No Turn creation |
| `ReplaceUserMessageAsync` | `EditAndResendAsync(conversationId, messageIndex, newContent)` | Truncate session at index, replace user message |
| `GetChatBubblesAsync` | `GetMessagesAsync` | Return `ChatMessage[]` directly from session |
| `GetTurnCountAsync` | — | Removed |

### ConversationAgentDispatcher

Simplified signature:

```csharp
Task StreamResponseAsync(
    Guid conversationId,
    string userMessage,       // complete message with attachment/skill refs baked in
    bool useThinking,
    IStreamSink sink,
    CancellationToken ct,
    string? circuitContextId = null);
```

Removed parameters: `turnId`, `attachedPaths`, `requestedSkillIds`, `truncateFromTurnId`, `userNameForTruncate`.

### AgentRunner

- Removed: `FirstMessageIndex` assignment, `IConversationSessionCoordinator` usage, `AgentTurnContext`
- Load/save session directly via `IAgentSessionStore`
- On cancellation: catch `OperationCanceledException` → `sessionStore.SaveAsync(current session)` → stream ends

### Interruption Flow

```
User clicks Stop → CancellationToken.Cancel
→ AgentRunner catches OperationCanceledException
→ sessionStore.SaveAsync(current session state)
→ Stream ends, UI shows content up to interruption point
```

## UI Layer — CLI Style

### Layout

```
ChatPage
├── ConversationSidebar (kept)
└── ChatContent
    └── ChatShell
        ├── MessageArea → CliMessageThread (replaces MessageThread)
        └── InputArea → ChatInput (simplified)
```

### CliMessageThread Visual

```
┌──────────────────────────────────────────────────┐
│ ❯ User message text...                    [↻] [✏️] │
│                                                  │
│ ◆ Assistant                                      │
│   Assistant reply with Markdown rendering...     │
│                                                  │
│   ┌─ 🔧 ReadFile ─────────────────────────┐      │
│   │ path: "/src/main.cs"                  │      │
│   │ ✓ Done (1.2s)                         │      │
│   └────────────────────────────────────────┘      │
│                                                  │
│   Continued reply text...                        │
│                                                  │
│ ❯ Second user message...                  [↻] [✏️] │
│                                                  │
│ ◆ Assistant                                      │
│   ...                                            │
└──────────────────────────────────────────────────┘
```

### Component Mapping

| New Component | Replaces | Responsibility |
|---------------|----------|----------------|
| `CliMessageThread` | `MessageThread` | Iterate messages, dispatch by role |
| `CliUserMessage` | `UserBubble` | User message line + action buttons (restart, edit) |
| `CliAssistantBlock` | `AssistantBubble` | Assistant content area (text + tools + reasoning) |
| `CliToolCall` | `ToolCallBlock` | Tool call card with border-left accent |
| `CliReasoningBlock` | `ReasoningBlock` | Collapsible thinking block |
| `MarkdownBlock` | (kept) | Markdown rendering unchanged |

### Styling

- Remove all `MudChat` / `MudChatBubble` dependencies
- Monospace/semi-monospace font, dark theme preferred
- Full-width content area, no left/right column split
- User and assistant separated by whitespace or thin divider
- Tool call cards use `border-left` accent, not full border
- Streaming: append text in current assistant area with cursor blink effect

### ChatPresentationService

- Remove `ChatBubble` model (`UserBubble`/`AssistantBubble` records)
- History: pass `ChatMessage[]` directly to `CliMessageThread`
- Streaming: `StreamUpdate → IBubbleBlock[]` conversion kept (tool call state tracking needed), but no "bubble" assembly

## Code Cleanup

### Files to Delete

| File | Reason |
|------|--------|
| `SmallEBot.Domain/Conversations/Metadata/TurnInfo.cs` | Turn abolished |
| `TurnInfoPersistence` in `ConversationMetadataPersistence.cs` | Turn abolished |
| Turn fields/methods in `ConversationMetadata` | Turn abolished |
| `IConversationSessionCoordinator` + implementation | Absorbed by Service/Runner |
| `ConversationBubbleHelper` | Bubble construction abolished |
| `ChatBubble.cs` (Core) | Bubble model abolished |
| `UserBubble.razor` / `AssistantBubble.razor` | Replaced by CLI components |
| `MessageThread.razor` | Replaced by `CliMessageThread` |
| Bubble-related methods in `ChatPresentationService` | Simplified |

### Data Migration

No explicit migration needed:

- **Read compatibility**: JSON deserialization ignores unknown `turns` field
- **Write overwrite**: Saving writes only new fields; old `turns` field disappears
- **session.json**: Unchanged format
