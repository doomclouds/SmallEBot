# Agent refactor design: context factory, tool factories, assembly, chat module

**Date:** 2026-02-15  
**Status:** Design validated (Sections 1–4). Ready for implementation plan.

## 1. Architecture overview

Four layers, top to bottom:

- **UI (existing):** `ChatArea` / `ChatPage` call only the Chat module API; they do not touch Agent, MCP, or Skills directly.
- **Chat module (new / extracted):** Single entry point for “one conversation action”: create turn, load history, call Agent streaming, persist assistant reply or error, refresh title. Depends on: Agent assembly, turn/message storage (existing DB + `ConversationService`), and optionally context estimation.
- **Agent assembly:** Only “build Agent from context + tools”. Input: system prompt (from context factory), tool list (built-in + MCP). Output: cached `AIAgent` (normal + thinking) and Invalidate. No knowledge of conversations, turns, or DB.
- **Supply layer:** Three stateless “factories”:
  - **Context factory:** Produces system prompt (fixed instructions + skills block) from `ISkillsConfigService`; optionally exposes the same string for token estimation.
  - **Built-in tool factory:** Returns `AITool[]` for GetCurrentTime, ReadFile.
  - **MCP tool factory:** From `IMcpConfigService`, creates stdio/HTTP clients and `ListToolsAsync`, returns `(AITool[], IAsyncDisposable[] clients)`; assembly or Chat owns disposal on Invalidate/Dispose.

Skills participate only in the context factory; MCP only in the tool list; built-in tools in their own factory; Agent is pure assembly; Chat is pure conversation orchestration.

---

## 2. Components and interfaces

**Context factory**  
- Interface: `IAgentContextFactory`, method `Task<string> BuildSystemPromptAsync(CancellationToken ct)`.  
- Implementation uses `ISkillsConfigService.GetMetadataForAgentAsync`, assembles fixed instructions + skills block (same logic as current `BuildSkillsBlock`).  
- Optional: `string? GetCachedSystemPrompt()` for token estimation; otherwise caller caches.

**Built-in tool factory**  
- Interface: `IBuiltInToolFactory`, method `AITool[] CreateTools()` (sync, stateless).  
- Implementation returns GetCurrentTime and ReadFile as AITool; ReadFile path whitelist and extensions stay inside the factory.

**MCP tool factory**  
- Interface: `IMcpToolFactory`, method `Task<(AITool[] Tools, IReadOnlyList<IAsyncDisposable> Clients)> LoadAsync(CancellationToken ct)`.  
- Implementation matches current stdio/HTTP branches in `EnsureToolsAsync`: read `IMcpConfigService.GetAllAsync`, create `McpClient`, `ListToolsAsync`, return tools and clients to be disposed on Invalidate/Dispose.  
- Can reuse or rename existing `McpToolsLoaderService` for this interface.

**Agent assembly**  
- Interface/class: `IAgentAssembly` or `AgentAssembly`.  
- Methods: `Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)` (get system prompt from context factory, built-in + MCP tools, create/cache two agents); `Task InvalidateAsync()` (clear cache, Dispose MCP clients); `int GetContextWindowTokens()`; optional `string? GetCachedSystemPromptForTokenCount()`.  
- Depends on: `IAgentContextFactory`, `IBuiltInToolFactory`, `IMcpToolFactory`, `IConfiguration`. No `AppDbContext` or `ConversationService`.

**Chat module**  
- Interface: `IChatAgentService` or `IConversationAgentService`. Conversation-level API:  
  - `Task<Guid> CreateTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, ct)`  
  - `IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(conversationId, userMessage, useThinking, ct)`  
  - `Task CompleteTurnWithAssistantAsync(conversationId, turnId, segments, ct)`  
  - `Task CompleteTurnWithErrorAsync(conversationId, turnId, errorMessage, ct)`  
  - `Task<double> GetEstimatedContextUsageAsync(conversationId, ct)`  
  - `int GetContextWindowTokens()`  
  - `Task InvalidateAgentAsync()` (forward to assembly)  
- Implementation depends on: `IAgentAssembly`, `AppDbContext` (or abstract message/turn storage), `ConversationService`, `ITokenizer`, `IConfiguration`.  
- Internally: CreateTurn/CompleteTurn write DB; SendMessageStreaming loads history, converts to framework messages, calls `GetOrCreateAgentAsync`, `RunStreamingAsync`, maps content to `StreamUpdate`; title generation stays in this module using the same agent once via `RunAsync`.  
- `GetEstimatedContextUsageAsync` uses assembly’s `GetCachedSystemPromptForTokenCount()` (or context factory cache) + `ChatMessageStoreAdapter` + tokenizer to compute ratio.

**UI**  
- `ChatArea` / `ChatPage` depend only on the Chat module interface (and existing `ConversationService`). They no longer depend on the current monolith `AgentService`; the Chat module implementation can keep the type name `AgentService` if desired.

---

## 3. Data flow and call order

**Send message and stream**  
1. UI calls Chat module `CreateTurnAndUserMessageAsync(...)`.  
2. Chat module writes DB: create `ConversationTurn`, insert user `ChatMessage`; if first message, call title generation (one `RunAsync` via assembly), then `SaveChangesAsync`, return `turnId`.  
3. UI calls `SendMessageStreamingAsync(...)`.  
4. Chat module: load history with `ChatMessageStoreAdapter`, convert to framework `ChatMessage` list and append current user message; call `IAgentAssembly.GetOrCreateAgentAsync(useThinking, ct)`; run `RunStreamingAsync(messages, ...)`, map content to `StreamUpdate` and yield.  
5. UI consumes `StreamUpdate` and maintains segments for persist.  
6. On stream end or user stop: UI calls `CompleteTurnWithAssistantAsync(...)` or `CompleteTurnWithErrorAsync(...)`; Chat module writes DB and `SaveChangesAsync`.  
7. When MCP or Skills config changes: UI or settings calls `InvalidateAgentAsync()`; Chat forwards to `IAgentAssembly.InvalidateAsync()`; assembly clears cache and Disposes MCP clients; next send rebuilds agent.

**Context usage**  
- UI calls `GetEstimatedContextUsageAsync(conversationId, ct)`.  
- Chat module uses assembly’s (or context factory’s) cached system prompt, loads messages, serializes to request-shaped JSON, passes to `ITokenizer.CountTokens`, divides by `GetContextWindowTokens()`, returns 0–1.  
- If no cache yet (e.g. before first GetOrCreateAgent), use default/base prompt or first `BuildSystemPromptAsync` result for estimation.

**Dependency direction:** UI → Chat module → assembly and storage; assembly uses context + built-in + MCP factories. Config UIs only call `InvalidateAgentAsync()`.

---

## 4. Error handling and edge cases

**Single MCP failure**  
- MCP tool factory try/catch per entry; on failure log Warning, skip that entry, continue; return tools and clients for successful entries.  
- Assembly builds agent with built-in + successful MCP tools; one failing MCP does not fail the whole load.

**Missing API key**  
- Assembly reads env in `GetOrCreateAgentAsync`; if empty, log Warning and still create client (e.g. empty string); first real request fails with auth error, Chat module catches and calls `CompleteTurnWithErrorAsync`.

**Stream interrupted**  
- On cancel or exception, Chat module persists: if there are segments, `CompleteTurnWithAssistantAsync` with partial content; else `CompleteTurnWithErrorAsync` with “Stopped by user” or error message. Every turn ends with either assistant content or an error message.

**No cache for context estimation**  
- Before any `GetOrCreateAgentAsync`, use context factory’s `BuildSystemPromptAsync` result (or base instructions only) for token count; or return 0 / “—” in UI so no exception.

**Invalidate and concurrency**  
- `InvalidateAsync`: Dispose MCP clients, then clear agent cache. In-flight requests keep using old agent until they finish; next request gets new agent. No mandatory “wait for current request” unless implemented later (e.g. lock or version).

**Dispose**  
- Chat module or root service that holds assembly is disposed on shutdown; assembly Disposes its MCP clients and clears agent references. Stateless factories need no IDisposable unless they hold non-managed resources.
