# DDD Core + Infrastructure + Repository design

**Date:** 2026-02-16  
**Status:** Design validated (Sections 1–3). Ready for implementation plan.

## 1. Architecture overview

**Goals:** Clear code boundaries and dependencies (core domain vs host), infrastructure (EF Core, file system) encapsulated separately, and direct database access replaced with a repository pattern so that future hosts (Blazor, Cron, other entry points) depend only on Core and injected infrastructure.

**Project layout:**

- **SmallEBot.Core** (class library)  
  Domain models, repository and core service **interfaces** (e.g. `IConversationRepository`, `IAgentRunner`, `IConversationService`). No reference to EF or Blazor; all persistence and external capabilities are consumed via interfaces.

- **SmallEBot.Infrastructure** (class library)  
  Implements repository and infrastructure interfaces: EF Core (`DbContext`, migrations in this project), file-based MCP/Skills config, preferences. References **Core** only; no Blazor. Current `ConversationService` / `ChatMessageStoreAdapter` logic that touches `AppDbContext` moves behind `IConversationRepository` implemented here.

- **SmallEBot** (existing Blazor Server app)  
  Host only: UI, SignalR, DI registration of Core interfaces with Infrastructure implementations. References **Core** and **Infrastructure**; no direct EF or repository implementation references in UI or app services.

**Dependency direction:** SmallEBot → Infrastructure → Core. Core does not depend on Infrastructure or the host.

---

## 2. Repository boundary and Data/Services mapping

**Aggregate root:** A single aggregate root **Conversation** is used. `ConversationTurn`, `ChatMessage`, `ToolCall`, and `ThinkBlock` belong to this aggregate; all current usage loads or deletes by conversation. One aggregate → one repository: **IConversationRepository** only (no separate repositories for Turn/Message).

**IConversationRepository (in Core):**

- Read: `Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct)`
- Read: `Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct)`
- Read: `Task<List<ChatMessage>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken ct)` (replaces `ChatMessageStoreAdapter.LoadMessagesAsync`)
- Read: `Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct)`
- Write: `Task<Conversation> CreateAsync(string userName, string title, CancellationToken ct)`
- Write: `Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct)`
- Write: `Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, CancellationToken ct)` (creates Turn + user message, optionally updates conversation title; returns turnId)
- Write: `Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct)`
- Write: `Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct)`

Child entities (Turn/Message/ToolCall/ThinkBlock) are not exposed via separate repository interfaces; the above write methods encapsulate all changes within the repository implementation and a single unit of work.

**Current type placement:**

| Current location | Target | Notes |
|------------------|--------|--------|
| `Data/Entities/*` | **Core** | Move as domain entities; keep POCO (EF attributes allowed in Core or applied in Infrastructure mapping). |
| `Data/AppDbContext` | **Infrastructure** | Rename to e.g. `SmallEBotDbContext` or keep name; migrations stay in Infrastructure. |
| `Data/Migrations/*` | **Infrastructure** | With DbContext. |
| `ConversationService` | **Split** | Pure logic (e.g. `GetChatBubbles`, `GetTimeline`) → Core (domain helper or extension). All DB access → call `IConversationRepository`; implementation in Infrastructure using DbContext. |
| `ChatMessageStoreAdapter` | **Absorbed** | Replaced by `IConversationRepository.GetMessagesForConversationAsync`; remove adapter; callers use repository. |
| `AgentService` | **Core (interface + impl) or Host** | Stops depending on `AppDbContext`; depends only on `IConversationRepository` and `IAgentBuilder`. For future Cron reuse, put interface and implementation in Core, implementation using `IConversationRepository`; Infrastructure only registers the implementation. |

**BackfillTurns:** Keep as one-off migration in **Infrastructure** (e.g. a small service or method that uses DbContext or repository); do not expose in Core’s public API.

---

## 3. Project naming, namespaces, and migration order

**Namespaces:**

- **Core:** `SmallEBot.Core` for the project; e.g. `SmallEBot.Core.Entities`, `SmallEBot.Core.Repositories`, `SmallEBot.Core.Models` (shared DTOs like `AssistantSegment`, `StreamUpdate` if used by interface). Keep existing model names (e.g. `Conversation`, `ChatMessage`) under `SmallEBot.Core.*`.
- **Infrastructure:** `SmallEBot.Infrastructure` (e.g. `SmallEBot.Infrastructure.Data`, `SmallEBot.Infrastructure.Repositories`). DbContext and migrations under `SmallEBot.Infrastructure.Data`; repository implementations under `SmallEBot.Infrastructure.Repositories`.
- **Host:** Keep `SmallEBot` for the Blazor app; `SmallEBot.Components`, `SmallEBot.Services` only for UI and host-specific wiring; host references Core and Infrastructure and composes DI.

**Package references:**

- **Core:** No EF, no Blazor, no MudBlazor. Only packages needed for domain (e.g. Microsoft.Extensions.AI / Agents if interfaces depend on framework types). Prefer minimal dependencies.
- **Infrastructure:** EF Core, EF SQLite, EF Design (for migrations); file/JSON access. References Core.
- **SmallEBot (host):** Blazor, MudBlazor, existing app packages; references Core + Infrastructure.

**Migration order (incremental, low risk):**

1. **Create Core project**  
   Add `SmallEBot.Core.csproj` (class library, net10.0). Move `Data/Entities/*` → Core (e.g. `SmallEBot.Core/Entities/`). Move shared models used by repository interface (e.g. `AssistantSegment`, `ChatBubble`, `TimelineItem`, `StreamUpdate`) to Core. Add repository interface `IConversationRepository` in Core with the method set above. Add solution reference from Host to Core.

2. **Create Infrastructure project**  
   Add `SmallEBot.Infrastructure.csproj` (class library). Move `AppDbContext` and `Data/Migrations` into Infrastructure; fix namespace to `SmallEBot.Infrastructure.Data`. Implement `ConversationRepository` in Infrastructure, implementing `IConversationRepository` and using the moved DbContext. Move `BackfillTurns` into Infrastructure (service or static helper using DbContext). Add project reference Infrastructure → Core; add Host → Infrastructure.

3. **Switch Host to repository**  
   In Host (and any service that currently uses DbContext for conversation/turn/message): replace `AppDbContext` and `ConversationService` direct DB usage with `IConversationRepository`. `ConversationService` becomes a thin facade or is inlined: read/write go through `IConversationRepository`; pure logic (e.g. `GetChatBubbles`) stays in Core as a static/domain helper used by Host or a small Core service. Remove `ChatMessageStoreAdapter`; callers use `IConversationRepository.GetMessagesForConversationAsync`. Register in Host: `AddDbContext` in Infrastructure extension or in Host pointing to Infrastructure’s DbContext; register `IConversationRepository` → `ConversationRepository` (scoped).

4. **Move Agent-related interfaces and implementations (optional phase)**  
   Move `IAgentBuilder`, `IAgentContextFactory`, `IBuiltInToolFactory`, `IMcpToolFactory` to Core (interfaces); implementations can stay in Infrastructure (or Host for Blazor-specific ones). `AgentService` (chat module) depends only on `IConversationRepository` and `IAgentBuilder`; its type can live in Core for reuse by Cron later. This step keeps the current behavior while establishing the boundary.

5. **Cleanup**  
   Remove any remaining direct `AppDbContext` usage from Host. Ensure EF migrations run from Infrastructure (e.g. `dotnet ef migrations add ... --project SmallEBot.Infrastructure --startup-project SmallEBot`). Update `AGENTS.md` and docs with new project layout and commands.

**Error handling:** Repository methods throw or return results (e.g. `bool` for `DeleteAsync`) as today; no new cross-cutting error model required. Host continues to handle exceptions in UI or middleware.

**Testing:** Core can be unit-tested with mock `IConversationRepository`. Infrastructure repository tests can use an in-memory SQLite or EF in-memory provider if desired later.

---

## Summary

- **Core:** Domain entities, `IConversationRepository`, shared models and pure conversation logic; no EF, no Blazor.
- **Infrastructure:** DbContext, migrations, `ConversationRepository`, file-based config (MCP, Skills, preferences), optional Backfill service.
- **Host:** Blazor app; references Core + Infrastructure; registers DI and uses only interfaces from Core. Future Cron or other entry points reference Core and the same Infrastructure (or a different implementation of Core interfaces).
