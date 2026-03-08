# Conversations DDD Refactoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate Conversations domain to DDD with dual-file storage (metadata.json + session.json), unify on Domain.ConversationMetadata, and remove SessionFileService/SessionManager.

**Architecture:** Domain holds ConversationMetadata aggregate with TurnInfo; Infrastructure implements IConversationMetadataRepository and IAgentSessionStore; Application layer introduces IConversationSessionCoordinator to orchestrate metadata + session lifecycle; Host removes old session services.

**Tech Stack:** .NET 10, Blazor Server, Microsoft.Agents.AI, System.Text.Json

---

## Task 1: Domain - Add SetFirstMessageIndexForTurn to ConversationMetadata

**Files:**
- Modify: `SmallEBot.Domain/Conversations/ConversationMetadata.cs`

**Step 1: Add method**

Add after `GetFirstMessageIndex`:

```csharp
/// <summary>
/// Sets the first message index for a turn (called after session is loaded, before agent runs).
/// </summary>
public void SetFirstMessageIndexForTurn(Guid turnId, int index)
{
    var turn = GetTurn(turnId);
    if (turn != null)
        turn.SetFirstMessageIndex(index);
}
```

**Step 2: Make TurnInfo.FirstMessageIndex mutable**

Modify: `SmallEBot.Domain/Conversations/TurnInfo.cs`

Change `FirstMessageIndex` from `init` to allow internal set. Add:

```csharp
public void SetFirstMessageIndex(int index) => _firstMessageIndex = index;
private int _firstMessageIndex;
// Update constructor to set _firstMessageIndex = firstMessageIndex
// Update property to return _firstMessageIndex
```

Or simpler: change `public int FirstMessageIndex { get; init; }` to `public int FirstMessageIndex { get; private set; }` and add `public void SetFirstMessageIndex(int index) => FirstMessageIndex = index;`

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Domain/Conversations/ConversationMetadata.cs SmallEBot.Domain/Conversations/TurnInfo.cs
git commit -m "feat(domain): add SetFirstMessageIndexForTurn for deferred index assignment"
```

---

## Task 2: Domain - Ensure TurnInfo JSON serialization

**Files:**
- Modify: `SmallEBot.Domain/Conversations/TurnInfo.cs`
- Modify: `SmallEBot.Domain/Conversations/ConversationMetadata.cs`

**Step 1: Add JSON attributes if needed**

ConversationMetadataRepository uses `JsonSerializer.Deserialize<ConversationMetadata>`. Domain.ConversationMetadata has `private readonly List<TurnInfo> _turns` and `IReadOnlyList<TurnInfo> Turns` (get-only). System.Text.Json will not populate `_turns` by default.

Options:
- Add `[JsonInclude]` and a private setter for a serialization-friendly backing, or
- Create a persistence DTO in Infrastructure and map in repository

**Recommended:** Add `[JsonInclude] public IReadOnlyList<TurnInfo> Turns { get; private set; }` and remove the backing field, initializing in constructor. Or use a `ConversationMetadataPersistence` DTO in Infrastructure.

**Step 2: Implement persistence DTO (simpler)**

Create: `SmallEBot.Infrastructure/Persistence/Conversations/ConversationMetadataPersistence.cs`

```csharp
namespace SmallEBot.Infrastructure.Persistence.Conversations;

internal sealed class ConversationMetadataPersistence
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CompressedContext { get; set; }
    public DateTime? CompressedAt { get; set; }
    public List<TurnInfoPersistence> Turns { get; set; } = [];
}

internal sealed class TurnInfoPersistence
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int FirstMessageIndex { get; set; }
    public List<string> AttachedPaths { get; set; } = [];
    public List<string> RequestedSkillIds { get; set; } = [];
}
```

**Step 3: Update ConversationMetadataRepository to use DTO**

Modify: `SmallEBot.Infrastructure/Persistence/Repositories/ConversationMetadataRepository.cs`

- Deserialize to `ConversationMetadataPersistence`, map to `ConversationMetadata`
- Serialize from `ConversationMetadata` by mapping to `ConversationMetadataPersistence`

Add mapping methods. Domain types have factory/constructor - need a way to reconstruct. ConversationMetadata has primary constructor; TurnInfo has primary constructor. Add static `FromPersistence` and `ToPersistence` or extension methods.

**Step 4: Build**

Run: `dotnet build`
Expected: Success

**Step 5: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/
git commit -m "feat(infra): add persistence DTO for ConversationMetadata JSON"
```

---

## Task 3: Application.Contracts - Add IConversationSessionCoordinator

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/IConversationSessionCoordinator.cs`

**Step 1: Define interface**

```csharp
using Microsoft.Agents.AI;
using SmallEBot.Domain.Conversations;

namespace SmallEBot.Application.Contracts.Conversations;

public interface IConversationSessionCoordinator
{
    Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default);

    Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        ConversationMetadata metadata,
        AIAgent agent,
        CancellationToken ct = default);
}
```

Note: Application.Contracts must reference Domain for ConversationMetadata. Check project references.

**Step 2: Add Domain reference to Application.Contracts if missing**

Run: `dotnet add SmallEBot.Application.Contracts reference SmallEBot.Domain`
(Only if not already referenced.)

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/IConversationSessionCoordinator.cs
git commit -m "feat(contracts): add IConversationSessionCoordinator"
```

---

## Task 4: Application - Implement ConversationSessionCoordinator

**Files:**
- Create: `SmallEBot.Infrastructure/Conversation/ConversationSessionCoordinator.cs`

**Step 1: Implement**

```csharp
using Microsoft.Agents.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Domain.Conversations;
using SmallEBot.Infrastructure.Persistence.AgentSession;

namespace SmallEBot.Application.Conversations;

public sealed class ConversationSessionCoordinator(
    IConversationMetadataRepository metadataRepository,
    IAgentSessionStore sessionStore,
    AgentSessionSerializer serializer) : IConversationSessionCoordinator
{
    public async Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, ct);
        if (metadata == null)
        {
            metadata = ConversationMetadata.Create(userName);
            metadata = metadata with { Id = conversationId }; // If needed, or use Create overload
            await metadataRepository.SaveAsync(metadata, ct);
        }

        var session = await sessionStore.LoadAsync(conversationId, ct);
        if (session == null)
        {
            session = await agent.CreateSessionAsync(ct);
        }
        else
        {
            // Deserialize from store - sessionStore returns AIAgentSession
            // AgentSessionStore loads JSON and uses serializer - but serializer needs agent
            // Re-check: AgentSessionStore.LoadAsync returns deserialized session
        }

        return (session!, metadata);
    }

    public async Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        ConversationMetadata metadata,
        AIAgent agent,
        CancellationToken ct = default)
    {
        await sessionStore.SaveAsync(conversationId, session, ct);
        await metadataRepository.SaveAsync(metadata, ct);
    }
}
```

Note: ConversationMetadata.Create returns new instance with Guid.NewGuid(). For GetOrCreateSessionAsync when metadata is null, we need to create with conversationId. Add overload: `ConversationMetadata.CreateWithId(conversationId, userName)` or similar.

**Step 2: Fix Create flow**

Domain.ConversationMetadata.Create uses Guid.NewGuid(). For new conversation we need Id = conversationId. Add:

```csharp
public static ConversationMetadata CreateWithId(Guid id, string userName, string title = "New conversation")
{
    return new ConversationMetadata(id, title, userName, DateTime.UtcNow);
}
```

**Step 3: Fix AgentSessionStore usage**

AgentSessionStore is in Infrastructure. Application references Infrastructure? Check CLAUDE.md - Application may not reference Infrastructure. Typical DDD: Application does NOT reference Infrastructure. Coordinator should be in Infrastructure or a separate composition root.

**Re-evaluate:** IConversationSessionCoordinator is an application concern (orchestration). Its implementation needs IConversationMetadataRepository (Domain interface, implemented in Infra) and IAgentSessionStore (Infra). So the implementation needs to live where it can see both - that's either Application (if App references Infra) or Infrastructure.

From CLAUDE.md: "Application → Core, Application" and "Infrastructure → Core". So Application does NOT reference Infrastructure. The coordinator implementation must go in Infrastructure. Create `SmallEBot.Infrastructure/Conversation/ConversationSessionCoordinator.cs`.

**Step 4: Move to Infrastructure**

Create: `SmallEBot.Infrastructure/Conversation/ConversationSessionCoordinator.cs`

Application.Contracts defines the interface. Infrastructure implements it. Application (AgentConversationService) depends on IConversationSessionCoordinator - injected from Infrastructure.

**Step 5: Build**

Run: `dotnet build`
Expected: Success

**Step 6: Commit**

```bash
git add SmallEBot.Application/ SmallEBot.Infrastructure/Conversation/ SmallEBot.Domain/
git commit -m "feat: implement ConversationSessionCoordinator"
```

---

## Task 5: Infrastructure - Update AgentSessionReader to use IAgentSessionStore

**Files:**
- Modify: `SmallEBot.Application.Contracts/Session/IAgentSessionReader.cs` (if signature changes)
- Modify: `SmallEBot/Services/Session/AgentSessionReader.cs` (Host) or move to Infrastructure

**Step 1: Relocate AgentSessionReader**

AgentSessionReader parses JSON from SessionData. With dual storage, session is in session.json. IAgentSessionStore loads raw session (AIAgentSession). To get messages we need to either:
- Parse the serialized JSON from session.json, or
- Use AgentSession's API if it exposes messages

AgentSessionReader currently takes ISessionFileService and reads metadata.SessionData. New flow: read from IAgentSessionStore. AgentSessionStore returns AIAgentSession - we need to extract messages. AgentSessionSerializer serializes to JSON. So we need a way to get messages from AgentSession. Microsoft.Agents.AI.AgentSession - check if it has a Messages property or similar.

Alternatively, add to IAgentSessionStore: `Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct)` for raw JSON, and AgentSessionReader parses that. Or AgentSessionReader depends on IAgentSessionStore, loads session, and extracts messages - but AgentSession might not expose messages directly.

Simpler: Add `Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct)` to a new interface, or extend IAgentSessionReader. The reader would use IAgentSessionStore to load, then AgentSessionSerializer to get JSON? No - serializer needs agent. The store returns deserialized session. So we need to get messages from the session object. If AgentSession has a way to enumerate messages, use that. Otherwise we need to serialize again to parse - wasteful.

Check: AgentSessionReader.ParseMessages takes JsonElement sessionData. So it needs the raw JSON. IAgentSessionStore.LoadAsync returns AgentSession. We'd need to serialize it again to get JSON for parsing. Or we add a method to get raw JSON from store - the store reads from file, so it could return the raw string without deserializing. Add `Task<string?> GetSessionJsonAsync` to IAgentSessionStore for the reader. Or have a dedicated "session content reader" that reads the file and returns JSON. That keeps AgentSessionStore focused on AgentSession and adds a thin reader for message extraction.

**Simpler approach:** IAgentSessionStore already has the file path. Add `Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct)` that reads the raw file and returns JSON string. AgentSessionReader then uses that. But that duplicates file reading. Alternatively, AgentSessionReader moves to Infrastructure, depends on IAgentSessionStore, and we add a method to get raw JSON. IAgentSessionStore could have a GetRawJsonAsync that reads the file - used only by the reader. Implement in AgentSessionStore.

**Step 2: Add GetSessionJsonAsync to IAgentSessionStore**

```csharp
Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default);
```

**Step 3: Implement in AgentSessionStore**

Read file, return content. Return null if not exists.

**Step 4: Update AgentSessionReader**

- Depend on IAgentSessionStore instead of ISessionFileService
- GetMessagesAsync: call GetSessionJsonAsync, parse JSON, extract messages (same logic as before)
- GetUserMessageContentAsync: needs turnId and metadata to get FirstMessageIndex. So signature changes to `GetUserMessageContentAsync(Guid conversationId, Guid turnId, ConversationMetadata metadata, CancellationToken ct)` or we pass firstMessageIndex. The caller (AgentConversationService) has metadata when calling PrepareTurnForRegenerateAsync. So we could have `GetUserMessageContentAsync(conversationId, firstMessageIndex, ct)` - the caller looks up the index from metadata.

**Step 5: Update IAgentSessionReader**

```csharp
Task<string?> GetUserMessageContentAsync(
    Guid conversationId,
    int firstMessageIndex,
    CancellationToken ct = default);
```

Remove turnIndex. Callers pass firstMessageIndex from metadata.GetFirstMessageIndex(turnId).

**Step 6: Build**

Run: `dotnet build`
Expected: Fix any compile errors in callers (PrepareTurnForRegenerateAsync, etc.)

**Step 7: Commit**

```bash
git add SmallEBot.Infrastructure/ SmallEBot.Application.Contracts/Session/ SmallEBot/Services/Session/
git commit -m "feat: AgentSessionReader uses IAgentSessionStore, GetUserMessageContent by firstMessageIndex"
```

---

## Task 6: Application - Refactor AgentConversationService to use new dependencies

**Files:**
- Modify: `SmallEBot.Application/Conversations/AgentConversationService.cs`

**Step 1: Replace dependencies**

- Remove: ISessionFileService, ISessionManager
- Add: IConversationMetadataRepository, IConversationSessionCoordinator, IAgentSessionReader

**Step 2: Update CreateConversationAsync**

Use IConversationMetadataRepository. Create ConversationMetadata via CreateWithId or Create, save. Also create empty session via coordinator? Or coordinator creates both when GetOrCreateSessionAsync is first called. For CreateConversation we only need metadata - session is created on first message. So: metadataRepository save new metadata. No session yet.

**Step 3: Update GetConversationsAsync, SearchConversationsAsync, GetConversationAsync, DeleteConversationAsync**

Use IConversationMetadataRepository. Map Domain.ConversationMetadata to Core.Entities.Conversation (or keep returning that DTO for UI). We may keep Core.Entities.Conversation as a DTO for now to minimize UI changes.

**Step 4: Update CreateTurnAndUserMessageAsync**

Load metadata from IConversationMetadataRepository. Add turn with firstMessageIndex: 0. Save. Generate title if first turn.

**Step 5: Update StreamResponseAndCompleteAsync**

Use IConversationSessionCoordinator.GetOrCreateSessionAsync. Get messages count from session (via IAgentSessionReader.GetMessagesAsync or similar). Update last turn's FirstMessageIndex. Save metadata. Run agent. Persist via coordinator.

**Step 6: Update PrepareTurnForRegenerateAsync, ReplaceUserMessageAsync, CompactConversationAsync**

Use metadata repository and session reader with firstMessageIndex.

**Step 7: Build**

Run: `dotnet build`
Expected: Success

**Step 8: Commit**

```bash
git add SmallEBot.Application/Conversations/AgentConversationService.cs
git commit -m "refactor(app): AgentConversationService uses repository and coordinator"
```

---

## Task 7: Host - Update AgentRunnerAdapter to use IConversationSessionCoordinator

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Replace ISessionAgentManager with IConversationSessionCoordinator**

Coordinator returns (Session, Metadata). Adapter needs session for RunStreamingAsync and metadata for PersistSessionAsync. PersistSessionAsync(session, metadata) - so we need to pass metadata through. After streaming, call coordinator.PersistSessionAsync(conversationId, session, metadata, agent, ct). But we don't have metadata in the adapter - we get (session, metadata) from GetOrCreateSessionAsync. Store metadata in a variable and pass to PersistSessionAsync at the end.

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "refactor(host): AgentRunnerAdapter uses IConversationSessionCoordinator"
```

---

## Task 8: Host - Update DI and remove old services

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Remove SessionFileService, SessionManager**

Remove registrations for ISessionFileService, SessionManager, ISessionManager, ISessionAgentManager.

**Step 2: Add ConversationSessionCoordinator**

Register in Infrastructure: `services.AddScoped<IConversationSessionCoordinator, ConversationSessionCoordinator>()`

**Step 3: Update AgentSessionReader registration**

AgentSessionReader is in Host. It now depends on IAgentSessionStore. Ensure IAgentSessionStore is registered (it is, in Infrastructure). Register AgentSessionReader in Host - it needs IAgentSessionStore. Host references Infrastructure? Check - Host references Application, Infrastructure. So Host can inject IAgentSessionStore.

**Step 4: Build**

Run: `dotnet build`
Expected: Success

**Step 5: Delete SessionFileService, SessionManager**

Delete: `SmallEBot/Services/Session/SessionFileService.cs`, `SmallEBot/Services/Session/SessionManager.cs`

Update ISessionManager, ISessionAgentManager - remove or replace usages. ISessionAgentManager is replaced by IConversationSessionCoordinator. ISessionManager - check usages. ISessionFileService - remove. IAgentSessionReader - keep, used by AgentConversationService.

**Step 6: Build**

Run: `dotnet build`
Expected: Fix any remaining references

**Step 7: Commit**

```bash
git add SmallEBot/Extensions/ SmallEBot.Infrastructure/ SmallEBot/Services/Session/
git commit -m "chore: remove SessionFileService and SessionManager, wire coordinator"
```

---

## Task 9: Core - Remove ConversationMetadata and TurnMetadata

**Files:**
- Delete: `SmallEBot.Core/Models/ConversationMetadata.cs`
- Delete: `SmallEBot.Core/Models/TurnMetadata.cs`
- Modify: All files referencing Core.Models.ConversationMetadata or Core.Models.TurnMetadata

**Step 1: Find all references**

Run: `rg "ConversationMetadata|TurnMetadata" --type cs`
Update each to use Domain.Conversations.ConversationMetadata and Domain.Conversations.TurnInfo.

**Step 2: Update Core.Entities.Conversation**

If used as return type for IAgentConversationService, either keep as DTO (map from Domain) or change interface to return Domain type. Prefer keeping DTO for UI boundary - map in AgentConversationService.

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add -A
git commit -m "chore: remove Core ConversationMetadata and TurnMetadata, use Domain types"
```

---

## Task 10: Verify and document

**Step 1: Run application**

Run: `dotnet run --project SmallEBot`
Expected: App starts, create new conversation, send message, verify no errors.

**Step 2: Update CLAUDE.md**

Update runtime data paths: `.agents/conversations/` instead of `.agents/sessions/`.

**Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for conversations storage path"
```

---

## Execution Handoff

Plan complete and saved to `docs/plans/2026-03-08-conversations-ddd-refactoring-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** – I dispatch a fresh subagent per task, review between tasks, fast iteration.

2. **Parallel Session (separate)** – Open a new session with executing-plans, batch execution with checkpoints.

**Which approach?**
