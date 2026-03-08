# Blazor Server architecture review: problems and design

**Date:** 2026-02-16  
**Status:** Design (architecture analysis + test checklist). No automated test project; test checklist for manual/business testing.

---

## 1. Current architecture and problems

**Current flow**  
Single Blazor Server app: UI (ChatArea, McpConfigDialog, SkillsConfigDialog) injects concrete `AgentService`. AgentService depends on `AppDbContext`, `ConversationService`, `IAgentBuilder`, `ITokenizer`. Agent building is already split into context/tool factories and `AgentBuilder` per `2026-02-15-agent-refactor-design.md`.

**Problems**

1. **Chat module has no interface**  
   UI and config dialogs depend on concrete `AgentService`. Design doc suggests `IChatAgentService`; adding an interface would improve testability and allow swapping implementations.

2. **Storage not abstracted**  
   AgentService directly uses `AppDbContext` and instantiates `ChatMessageStoreAdapter(db, conversationId)` internally. No `IMessageStore` or similar; future storage changes or testing would require touching the chat module.

3. **ConversationService mixes concerns**  
   It does CRUD (Create, Get, Delete) and domain assembly (`GetChatBubbles`, `GetTimeline`). The latter is pure domain logic; moving it to a dedicated type (e.g. `ConversationBubbleBuilder` or domain service) would make it reusable and easier to test if needed later.

4. **Startup and config**  
   `BackfillTurnsAsync` runs synchronously at startup via `GetAwaiter().GetResult()`; large DBs could delay startup. `appsettings.json` has unused `ConnectionStrings:DefaultConnection` while the app uses BaseDirectory + `smallebot.db`; remove or document to avoid confusion.

5. **No test project**  
   Repo has no tests; Chat module’s concrete dependencies make unit testing costly. This design does not add a test project; Section 5 provides a **test checklist** for manual/business testing.

---

## 2. Recommended layering and dependencies

**Direction**

- **UI** → depends only on Chat module **interface** (e.g. `IChatAgentService`) and existing `ConversationService` for list/delete.
- **Chat module** → implements interface; depends on `IAgentBuilder`, `ConversationService`, and either `AppDbContext` or a small **conversation message loader** abstraction (see below).
- **AgentBuilder** → unchanged; depends on context + built-in + MCP factories, no DbContext.

**Optional storage abstraction**  
If you want to avoid Chat module touching `AppDbContext` directly, introduce something like:

- `IConversationMessageLoader`: `Task<List<ChatMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken ct)`  
- Implemented by a thin adapter over `AppDbContext` (current `ChatMessageStoreAdapter` logic). Chat module then depends on `IConversationMessageLoader` instead of `AppDbContext` for history loading. Create turn / complete turn can stay on `ConversationService` or a small write-side abstraction if you prefer. YAGNI: only add this if you plan multiple backends or need to mock storage for tests.

**ConversationService split (optional)**  
Keep CRUD in `ConversationService`. Optionally move `GetChatBubbles` / `GetTimeline` into a stateless helper (e.g. `ConversationBubbleBuilder`) that takes a loaded `Conversation` and returns bubbles/timeline. `ConversationService` would call this helper so behavior stays the same; UI still uses `ConversationService` for “get conversation + bubbles.” Benefit: domain logic is in one place and reusable from API or other callers later.

**Config and startup**  
- Use one source of truth for DB path (BaseDirectory + `smallebot.db`); remove or clearly document `ConnectionStrings:DefaultConnection` in appsettings.  
- Consider running `BackfillTurnsAsync` in a fire-and-forget background task after app start, or document that it runs once and may block startup on large DBs.

---

## 3. Chat module interface (recommended)

Define an interface used by UI and config dialogs; keep current `AgentService` as the implementation.

- `Task<Guid> CreateTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, CancellationToken ct)`
- `IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(Guid conversationId, string userMessage, bool useThinking, CancellationToken ct)`
- `Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct)`
- `Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct)`
- `Task<double> GetEstimatedContextUsageAsync(Guid conversationId, CancellationToken ct)`
- `Task InvalidateAgentAsync()`

Register in DI: `builder.Services.AddScoped<IChatAgentService, AgentService>()`. Inject `IChatAgentService` in `ChatArea`, `McpConfigDialog`, `SkillsConfigDialog`. No change to existing behavior; only abstraction and dependency direction.

---

## 4. Error handling and edge cases

Align with existing agent-refactor design:

- **Single MCP failure:** Log and skip that MCP; builder uses remaining tools.
- **Missing API key:** Builder logs warning; first request fails with auth error; Chat completes turn with error.
- **Stream interrupted (cancel/exception):** Persist partial content via `CompleteTurnWithAssistantAsync` when segments exist; otherwise `CompleteTurnWithErrorAsync` with message. Every turn ends with either assistant content or error.
- **Context estimation before first agent build:** Use fallback system prompt (current behavior) or “—” in UI; no exception.

See `2026-02-15-agent-refactor-design.md` Section 4 for Invalidate/concurrency and Dispose.

---

## 5. Test checklist (manual / business testing)

Use this list to verify behavior when changing architecture or features. Not an automated test suite.

**Conversation and identity**

- [ ] First visit: username dialog appears; after submitting, sidebar shows conversation list.
- [ ] New conversation: title defaults to “新对话” or first-message-based title after first send.
- [ ] Conversation list: ordered by last updated; selecting a conversation loads messages and context %.

**Send and stream**

- [ ] Send message: user bubble appears immediately; assistant streams text; after completion, assistant bubble is persisted and remains after refresh.
- [ ] Thinking mode off: no reasoning block (or reasoning collapsed); reply is normal text/tool calls.
- [ ] Thinking mode on: reasoning block appears (expandable); reply segments and tool calls display correctly.
- [ ] Stop during stream: streaming stops; turn is completed with partial content or “Stopped by user” so no duplicate/ghost bubble after refresh.
- [ ] Long conversation: context % updates and displays (e.g. “—”, “12.3%”); no crash when history is large.

**Tool calls and built-in tools**

- [ ] GetCurrentTime: agent can be asked for current time; tool call appears (if ShowToolCalls); result shown.
- [ ] ReadFile: ask to read a file under run directory with allowed extension; tool call and content appear. Path outside whitelist is rejected.
- [ ] ReadSkill: ask about a skill by name; skill content is used in reply.

**MCP**

- [ ] MCP config dialog: add HTTP MCP (e.g. docs MCP); save; send message that uses that MCP; tools from MCP appear and work.
- [ ] Enable/disable MCP: disable an MCP; send message; that MCP’s tools are not available. Re-enable; next message uses them again (after invalidate).
- [ ] One MCP fails to connect: other MCPs still load; conversation works with remaining tools; no hard failure.

**Skills**

- [ ] Skills config: add/edit/import skill; list shows correct name/description. After change, call `InvalidateAgentAsync()` (e.g. from Skills dialog); next message uses updated skills block.
- [ ] System vs user skills: both appear in agent context as designed; ReadSkill tool can read by id.

**Preferences and theme**

- [ ] Theme: switch light/dark; theme persists and restores on reload; no flash of wrong theme.
- [ ] Username: change in settings; new conversations use new name; existing list scoped by previous name until refresh/re-login as designed.
- [ ] Show tool calls: toggle; assistant bubbles show or hide tool call blocks accordingly.
- [ ] Use thinking mode: toggle; next message uses thinking or normal model as selected.

**Errors and edge cases**

- [ ] No API key: clear `ANTHROPIC_API_KEY` / `DeepseekKey`; send message; error message persisted (e.g. “Error: …”); no unhandled exception.
- [ ] Conversation not found: e.g. delete conversation in another tab then send in current tab; graceful error or refresh.
- [ ] Network/server error during stream: error message persisted; UI shows error state; next message can be sent.

**Config and invalidation**

- [ ] After MCP add/edit/delete/toggle: next send uses new tool set (invalidates agent).
- [ ] After skill add/remove/import: next send uses new skills in system prompt (invalidates agent).

**Data and persistence**

- [ ] Delete conversation: removed from list; selecting it or reload shows empty or 404 as designed.
- [ ] App restart: conversations and messages persist; DB path is under run directory (or as configured).
- [ ] Migrations: new migration applied on startup; no duplicate or failed migration on second start.

---

## 6. Solution approaches (per problem)

### 6.1 Chat module has no interface

| Approach | What to do | Trade-off |
|----------|------------|-----------|
| **A. Add interface only** | Define `IChatAgentService` with current AgentService surface; register `AddScoped<IChatAgentService, AgentService>()`; change `@inject AgentService` → `@inject IChatAgentService` in ChatArea, McpConfigDialog, SkillsConfigDialog. | Low risk, small change, immediate benefit for dependency direction and future mocking. No behavior change. |
| **B. Interface + rename** | Same as A, and rename `AgentService` → `ChatAgentService` (or keep name, implement interface). | Slightly more diff; name can better reflect “chat orchestration” if you want. |
| **C. Defer** | Do nothing until you need to unit-test the chat module or swap implementation. | No cost now; when you need it, you add the interface in one pass. |

**Recommendation:** **A**. One-time, low-effort change; aligns with design doc and improves structure without YAGNI. B is optional naming polish.

---

### 6.2 Storage not abstracted

| Approach | What to do | Trade-off |
|----------|------------|-----------|
| **A. Keep as-is** | AgentService keeps using `AppDbContext` and `new ChatMessageStoreAdapter(db, conversationId)` internally. | No code churn. If you never need another storage or to mock history load, this is fine. |
| **B. Introduce IConversationMessageLoader** | New interface: `Task<List<Data.Entities.ChatMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken ct)`. Implement with current `ChatMessageStoreAdapter` logic; inject a scoped factory or pass `conversationId` when resolving (e.g. custom factory per request). AgentService depends on loader instead of DbContext for history only; CreateTurn/CompleteTurn still use `ConversationService` or DbContext. | Chat module no longer touches DbContext for read path; easier to mock for tests or swap storage later. Slightly more types and DI. |
| **C. Full repository** | Add e.g. `IConversationRepository` with LoadMessages, AppendTurn, CompleteTurn, etc. | Maximum flexibility and testability, but more abstraction than you need if you only have one store. |

**Recommendation:** **A** for now (YAGNI). Add **B** only when you introduce tests that need to mock history, or plan a second storage backend.

---

### 6.3 ConversationService mixes concerns

| Approach | What to do | Trade-off |
|----------|------------|-----------|
| **A. Leave as-is** | Keep `GetChatBubbles` and `GetTimeline` inside `ConversationService`. | No change. Logic is already static and pure given a loaded `Conversation`; only “placement” is mixed with CRUD. |
| **B. Extract stateless helper** | New class e.g. `ConversationBubbleBuilder` (or `ConversationToBubblesMapper`) with static or instance methods: input `Conversation`, output `List<ChatBubble>` and timeline. `ConversationService.GetByIdAsync` loads conversation, then calls this helper and returns; `GetChatBubbles(conv)` becomes a one-liner to the helper. | Clear separation: CRUD vs domain assembly. Same behavior; helper is reusable from API or other callers. One extra type. |
| **C. Move to domain layer** | Put bubble/timeline logic in a `Domain` or `Core` project and have both UI and services depend on it. | Good if you later split into multiple projects; overkill for a single-app codebase right now. |

**Recommendation:** **A** unless you want clearer boundaries; then **B** (extract helper, keep calling it from ConversationService). Avoid C until you have multiple projects.

---

### 6.4 Startup and config

| Approach | What to do | Trade-off |
|----------|------------|-----------|
| **A. Document only** | Add a one-line comment in Program.cs that BackfillTurnsAsync runs once and may block on large DBs; in appsettings or README note that DB path is BaseDirectory + `smallebot.db` and `ConnectionStrings:DefaultConnection` is unused (or remove the key). | No behavior change; avoids future confusion. |
| **B. Single source of truth for DB path** | Read DB path from one place (e.g. baseDir in Program.cs, or a small `IDataPathProvider`). Remove `ConnectionStrings:DefaultConnection` from appsettings if nothing reads it. | Less duplication and fewer “which connection string?” questions. |
| **C. Backfill async** | After `app.Run()` is not possible, so either: (1) keep sync backfill but document, or (2) run backfill in a fire-and-forget task after `app.StartAsync()` / first request (e.g. background service that runs once). Option (2) avoids blocking startup but needs care: ensure only one run, and that first request doesn’t rely on backfill being done if you have legacy data. | (1) Simple. (2) Better startup time on large DBs; slightly more complexity and ordering concerns. |

**Recommendation:** **A + B** (document and remove or document unused config). For backfill: keep current sync behavior unless you hit slow startup; then consider **C(2)** with a one-shot background step.

---

### 6.5 Summary of recommended order

1. **Do first:** Add **IChatAgentService** and switch UI/config to it (Section 6.1 A). Optional: clean config and document backfill (Section 6.4 A/B).
2. **Do when needed:** Storage abstraction (6.2 B) when you need tests or a second store; ConversationService split (6.3 B) when you want clearer boundaries or reuse from API.
3. **Skip for now:** Full repository (6.2 C), domain project (6.3 C), unless the product grows.

---

## 7. Summary

- Introduce **IChatAgentService** and have UI/config depend on it; keep **AgentService** as implementation (Section 6.1 recommended first step).
- Optionally abstract **conversation message loading** (Section 6.2 B) and/or extract **Conversation bubble/timeline** logic (Section 6.3 B) when needed.
- Fix or document **connection string** and backfill behavior (Section 6.4); consider async backfill only if startup is slow.
- Use **Section 5 test checklist** for manual/business testing; no test project in this design.
