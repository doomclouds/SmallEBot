# Phase 4 Redesign: Minimal Metadata Storage

> **Status:** Approved
> **Date:** 2026-03-06
> **Related:** 2026-03-06-agent-framework-refactoring-design.md

## Problem Statement

The original Phase 4 plan required removing all database entities (ChatMessage, ToolCall, ThinkBlock, ConversationTurn) and migrating to file-based storage. However, the current implementation still relies on these entities for:

1. **UI Display** - Historical messages, tool calls, thinking blocks
2. **CompressionService** - Needs message/tool call data for summarization
3. **Token Estimation** - Calculates context usage from stored messages

## Discovery

Analysis of `AgentSession` serialized data revealed:

```json
{
  "stateBag": {
    "InMemoryChatHistoryProvider": {
      "messages": [
        {
          "role": "user",
          "contents": [{ "$type": "text", "text": "Hello" }]
        },
        {
          "authorName": "SmallEBot",
          "createdAt": "2026-03-06T05:05:19.2732123+00:00",
          "role": "assistant",
          "contents": [
            { "$type": "reasoning", "text": "...", "protectedData": "..." },
            { "$type": "text", "text": "..." }
          ]
        }
      ]
    }
  }
}
```

**Key findings:**
- ✅ AgentSession contains full message history
- ✅ Assistant messages have `createdAt` timestamp
- ✅ `contents` array includes text, reasoning, and function calls
- ❌ User messages lack `createdAt` and attachment metadata
- ❌ No turn grouping concept

## Design Decision

**Store only minimal metadata, read everything else from AgentSession.**

### Data Structure

**ConversationMetadata (extended):**
```json
{
  "id": "guid",
  "title": "Conversation title",
  "userName": "user",
  "createdAt": "2026-03-06T05:01:44Z",
  "updatedAt": "2026-03-06T05:06:01Z",
  "compressedContext": null,
  "compressedAt": null,
  "sessionData": { ... },
  "turns": [
    {
      "id": "turn-guid",
      "createdAt": "2026-03-06T05:05:19Z",
      "attachedPaths": ["file1.md"],
      "requestedSkillIds": ["skill-1"]
    }
  ]
}
```

**Turn Metadata Fields:**
| Field | Type | Purpose |
|-------|------|---------|
| `id` | Guid | Unique turn identifier |
| `createdAt` | DateTime | When user sent the message |
| `attachedPaths` | string[] | User-attached files |
| `requestedSkillIds` | string[] | User-requested skills |

### Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                      Data Sources                            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ConversationMetadata          AgentSession                 │
│  ┌─────────────────┐          ┌─────────────────┐          │
│  │ turns[]         │          │ messages[]      │          │
│  │ - id            │          │ - role          │          │
│  │ - createdAt     │          │ - contents[]    │          │
│  │ - attachedPaths │          │ - createdAt     │          │
│  │ - skillIds      │          │ - authorName    │          │
│  └────────┬────────┘          └────────┬────────┘          │
│           │                            │                    │
│           │       Merge by index       │                    │
│           └────────────┬───────────────┘                    │
│                        ▼                                    │
│              ┌─────────────────┐                            │
│              │  UI Bubble View │                            │
│              │  - User message │                            │
│              │  - Attachments  │                            │
│              │  - Assistant    │                            │
│              │  - Reasoning    │                            │
│              │  - Tool calls   │                            │
│              └─────────────────┘                            │
└─────────────────────────────────────────────────────────────┘
```

### Mapping Logic

1. **Turn to Message Pair:**
   - Turn[i] → User message at index i*2
   - Turn[i] → Assistant message at index i*2 + 1

2. **Bubble Construction:**
   ```
   UserBubble = {
     content: session.messages[i*2].contents[0].text,
     attachedPaths: turns[i].attachedPaths,
     createdAt: turns[i].createdAt
   }

   AssistantBubble = {
     items: session.messages[i*2+1].contents.map(c => ...),
     createdAt: session.messages[i*2+1].createdAt
   }
   ```

## Services to Update

### 1. ConversationMetadata.cs
- Add `Turns` property: `List<TurnMetadata>`

### 2. TurnMetadata.cs (new)
```csharp
public class TurnMetadata
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> AttachedPaths { get; set; } = [];
    public List<string> RequestedSkillIds { get; set; } = [];
}
```

### 3. ISessionFileService / SessionFileService
- Update to handle new `Turns` field

### 4. AgentConversationService
- `CreateTurnAndUserMessageAsync` → Add turn metadata instead of database insert
- `StreamResponseAndCompleteAsync` → Update turn metadata after completion
- Remove repository dependencies for message/turn operations

### 5. ChatPresentationService
- Read messages from AgentSession (via ISessionManager)
- Merge with turn metadata for attachments
- Remove entity dependencies

### 6. CompressionService
- Read messages from AgentSession instead of database entities
- Update interface to use `ChatMessage` from Microsoft.Extensions.AI

### 7. ICompressionService
- Change parameter types from entity types to AI types

## Files to Delete

After migration is complete:
- `SmallEBot.Core/Entities/ChatMessage.cs`
- `SmallEBot.Core/Entities/ToolCall.cs`
- `SmallEBot.Core/Entities/ThinkBlock.cs`
- `SmallEBot.Core/Entities/ConversationTurn.cs`
- `SmallEBot.Core/Repositories/IConversationRepository.cs`
- `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`
- `SmallEBot.Infrastructure/Data/SmallEBotDbContext.cs` (or remove entity DbSets)
- All EF Core migrations

## Migration Strategy

### Phase 4.1: Add Turn Metadata (non-breaking)
1. Create `TurnMetadata` class
2. Add `Turns` to `ConversationMetadata`
3. Update `SessionFileService` to handle new field

### Phase 4.2: Migrate Services
1. Update `AgentConversationService` to write turn metadata
2. Create `IAgentSessionReader` for reading session messages
3. Update `ChatPresentationService` to read from session

### Phase 4.3: Update Compression
1. Change `ICompressionService` interface
2. Update `CompressionService` implementation
3. Update token estimation

### Phase 4.4: Remove Database
1. Remove entity files
2. Remove repository files
3. Remove EF Core DbSets and migrations
4. Clean up DI registrations

## Benefits

1. **Minimal File Size** - Only store what AgentSession doesn't have
2. **Single Source of Truth** - AgentSession is authoritative for message content
3. **No Data Duplication** - Messages stored once, not in two places
4. **Simpler Architecture** - Fewer moving parts, easier to understand

## Trade-offs

1. **Runtime Merging** - UI must merge session data with metadata (slight complexity)
2. **Session Dependency** - UI needs AgentSession to display anything
3. **Index-based Mapping** - Turn-to-message mapping relies on message order
