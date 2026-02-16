# Agent refactor implementation plan

> **Reference:** Design `docs/plans/2026-02-15-agent-refactor-design.md`.

**Goal:** Split the monolith `AgentService` into: context factory (system prompt + skills), built-in tool factory, MCP tool factory, agent assembly, and chat module. UI and config dialogs keep calling the chat module (can remain type name `AgentService`); assembly composes context + tools and caches agent.

**Order:** Add factories and assembly first; then refactor `AgentService` into the chat module that uses assembly; finally wire DI. Each task ends with build and optional commit.

**Tech:** .NET 10, existing Anthropic/MCP/Skills stack. No new NuGet. `IMcpToolsLoaderService` stays for config UI; new `IMcpToolFactory` for agent tool loading (returns AITool[] + clients to hold).

---

## Task 1: Agent context factory

**Files:**
- Create: `SmallEBot/Services/AgentContextFactory.cs` (interface + implementation)

**Steps:**

1. Add `IAgentContextFactory` with:
   - `Task<string> BuildSystemPromptAsync(CancellationToken ct)`
   - `string? GetCachedSystemPrompt()` (for token estimation; set after first build)

2. Implement `AgentContextFactory`:
   - Inject `ISkillsConfigService`, `ILogger<AgentContextFactory>`.
   - Fixed base instructions string (same as current `AgentInstructions` in AgentService).
   - `BuildSystemPromptAsync`: call `skillsConfig.GetMetadataForAgentAsync(ct)`, build skills block (same logic as current `BuildSkillsBlock`: intro line + per-skill "id: name — description"), append to base instructions, cache result in a field, return.
   - `GetCachedSystemPrompt`: return cached value or null.

3. Register in `Program.cs`: `builder.Services.AddScoped<IAgentContextFactory, AgentContextFactory>()`.

4. Build and verify. Optional: commit `feat(agent): add AgentContextFactory for system prompt and skills block`.

---

## Task 2: Built-in tool factory

**Files:**
- Create: `SmallEBot/Services/BuiltInToolFactory.cs` (interface + implementation)

**Steps:**

1. Add `IBuiltInToolFactory` with `AITool[] CreateTools()`.

2. Implement `BuiltInToolFactory`:
   - Move `GetCurrentTime` and `ReadFile` from AgentService into this class (static or instance methods with `[Description(...)]`).
   - `ReadFile`: keep path validation (BaseDirectory, allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml).
   - `CreateTools()`: return `new[] { AIFunctionFactory.Create(GetCurrentTime), AIFunctionFactory.Create(ReadFile) }`.

3. Register in `Program.cs`: `builder.Services.AddSingleton<IBuiltInToolFactory, BuiltInToolFactory>()` (stateless).

4. Build and verify. Optional: commit `feat(agent): add BuiltInToolFactory for GetCurrentTime and ReadFile`.

---

## Task 3: MCP tool factory

**Files:**
- Create: `SmallEBot/Services/McpToolFactory.cs` (interface + implementation)

**Steps:**

1. Add `IMcpToolFactory` with:
   - `Task<(AITool[] Tools, IReadOnlyList<IAsyncDisposable> Clients)> LoadAsync(CancellationToken ct)`

2. Implement `McpToolFactory`:
   - Inject `IMcpConfigService`, `ILogger<McpToolFactory>`.
   - Copy the MCP loading loop from `AgentService.EnsureToolsAsync`: get all entries via `mcpConfig.GetAllAsync(ct)`, for each enabled entry try stdio or HTTP (same options as today), `McpClient.CreateAsync`, `ListToolsAsync`, add tools and client to lists. On exception log Warning and skip that entry.
   - Return `(tools.ToArray(), clients)`.

3. Register in `Program.cs`: `builder.Services.AddScoped<IMcpToolFactory, McpToolFactory>()`.

4. Build and verify. Optional: commit `feat(agent): add McpToolFactory returning AITool[] and clients`.

---

## Task 4: Agent assembly

**Files:**
- Create: `SmallEBot/Services/AgentAssembly.cs` (interface + implementation)

**Steps:**

1. Add `IAgentAssembly` with:
   - `Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)`
   - `Task InvalidateAsync()`
   - `int GetContextWindowTokens()`
   - `string? GetCachedSystemPromptForTokenCount()`

2. Implement `AgentAssembly`:
   - Inject `IAgentContextFactory`, `IBuiltInToolFactory`, `IMcpToolFactory`, `IConfiguration`, `ILogger<AgentAssembly>`.
   - Fields: `AIAgent? _agent`, `AIAgent? _agentWithThinking`, `List<IAsyncDisposable>? _mcpClients`, `int _contextWindowTokens` from config (e.g. `DeepSeek:ContextWindowTokens`, 128000).
   - `GetOrCreateAgentAsync(useThinking, ct)`:
     - Get system prompt: `await _contextFactory.BuildSystemPromptAsync(ct)` (this populates context factory cache).
     - Get tools: `builtIn.CreateTools()` and `await mcpToolFactory.LoadAsync(ct)`; combine into one list; store `_mcpClients` from MCP result.
     - Read API key (ANTHROPIC_API_KEY / DeepseekKey), base URL, model, thinkingModel from config (same keys as current AgentService).
     - If useThinking and _agentWithThinking != null return it; else if !useThinking and _agent != null return it.
     - Create AnthropicClient, AsAIAgent(model, name, instructions, tools); cache and return.
   - `InvalidateAsync()`: if _mcpClients != null, Dispose each, set null; _agent = null; _agentWithThinking = null. Optionally clear context factory cache if it exposes a clear method, or leave cache for token count until next build.
   - `GetContextWindowTokens()`: return _contextWindowTokens.
   - `GetCachedSystemPromptForTokenCount()`: return _contextFactory.GetCachedSystemPrompt() (so Chat module can use for estimation).

3. Register in `Program.cs`: `builder.Services.AddScoped<IAgentAssembly, AgentAssembly>()`.

4. Build and verify. Optional: commit `feat(agent): add AgentAssembly composing context and tools`.

---

## Task 5: Refactor AgentService into chat module

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Optionally add: `SmallEBot/Services/IChatAgentService.cs` (interface only; implementation stays AgentService)

**Steps:**

1. (Optional) Define `IChatAgentService` with the public surface used by UI: `CreateTurnAndUserMessageAsync`, `SendMessageStreamingAsync`, `CompleteTurnWithAssistantAsync`, `CompleteTurnWithErrorAsync`, `GetEstimatedContextUsageAsync`, `GetContextWindowTokens`, `InvalidateAgentAsync`. If you prefer minimal change, keep callers on `AgentService` and do not add the interface until later.

2. Change `AgentService` constructor to depend on: `AppDbContext db`, `ConversationService convSvc`, `IAgentAssembly assembly`, `ITokenizer tokenizer`, `IConfiguration config`, `ILogger<AgentService> log`. Remove `IMcpConfigService`, `ISkillsConfigService` (no longer used here).

3. Remove from AgentService: `EnsureToolsAsync`, `BuildSkillsBlock`, `AgentInstructions` constant, `GetCurrentTime`, `ReadFile`, `ReadFileAllowedExtensions`, all MCP and agent-building logic. Remove fields: `_agent`, `_agentWithThinking`, `_mcpClients`, `_cachedInstructionsForTokenCount`.

4. Implement by delegation:
   - `InvalidateAgentAsync()` → `await _assembly.InvalidateAsync()`.
   - `GetContextWindowTokens()` → `return _assembly.GetContextWindowTokens()`.
   - `GetOrCreateAgentAsync` no longer exists; internally use `await _assembly.GetOrCreateAgentAsync(useThinking, ct)` wherever an agent is needed (see below).
   - `SendMessageStreamingAsync`: get agent via `await _assembly.GetOrCreateAgentAsync(useThinking, ct)`; load history with `ChatMessageStoreAdapter`, build framework messages, run `agent.RunStreamingAsync(...)`, map content to `StreamUpdate` (same mapping as today). No direct tool or context building.
   - `CreateTurnAndUserMessageAsync`, `CompleteTurnWithAssistantAsync`, `CompleteTurnWithErrorAsync`: keep current DB logic unchanged (they do not touch agent/tools).
   - `GetEstimatedContextUsageAsync`: get system prompt from `_assembly.GetCachedSystemPromptForTokenCount()` (or base instructions if null); load messages with `ChatMessageStoreAdapter`; serialize to JSON (same `SerializeRequestJsonForTokenCount` + DTOs); tokenizer.CountTokens; divide by `_assembly.GetContextWindowTokens()`; return ratio. Keep `SerializeRequestJsonForTokenCount` and the two DTOs in AgentService (or move to a small static helper class).
   - `GenerateTitleAsync`: get agent with `await _assembly.GetOrCreateAgentAsync(false, ct)`, then same prompt and RunAsync as today.
   - `DisposeAsync`: no longer dispose DbContext (if it was; check current). Call `await _assembly.InvalidateAsync()` or nothing if assembly is not IDisposable; if AgentService holds no disposable beyond assembly, just suppress finalizer. Assembly owns MCP client disposal in InvalidateAsync.

5. Ensure `AgentService` no longer implements `IAsyncDisposable` if it has nothing to dispose; otherwise keep and dispose only what it owns. Check Program.cs: AgentService is scoped; DbContext is scoped—usually you do not dispose DbContext in a scoped service. Leave as-is if current code does not dispose db.

6. Build and verify. Run app: send message, open Skills/MCP config, invalidate, send again. Optional: commit `refactor(agent): AgentService delegates to AgentAssembly and factories`.

---

## Task 6: DI and call site check

**Files:**
- Modify: `Program.cs` (ensure all new services registered; remove any duplicate or obsolete registrations)
- Check: `ChatArea.razor`, `McpConfigDialog.razor`, `SkillsConfigDialog.razor` (inject AgentService or IChatAgentService; no code change if still AgentService)

**Steps:**

1. In `Program.cs`, confirm registration order: `IAgentContextFactory`, `IBuiltInToolFactory`, `IMcpToolFactory`, `IAgentAssembly`, then `AgentService`. All scoped except BuiltInToolFactory (singleton).

2. Confirm no caller uses `AgentService` for anything other than the chat-module API (CreateTurn, SendMessageStreaming, CompleteTurn, GetEstimatedContextUsage, GetContextWindowTokens, InvalidateAgentAsync). If any code referenced internal helpers, remove or replace.

3. Build and full manual test: new conversation, stream, stop, error path, context % display, MCP config change + invalidate, Skills config change + invalidate.

4. Optional: commit `chore(agent): wire Agent refactor DI and verify call sites`.

---

## Checkpoints

- After Task 4: Assembly can be unit-tested or exercised in isolation (e.g. minimal console or test that builds agent and runs one message).
- After Task 5: Existing UI and config flows work without change; only backend structure changed.
- After Task 6: No regressions; refactor complete.

## Notes

- `IMcpToolsLoaderService` remains for the MCP config dialog (single-server tool list for display). Do not remove or replace it; `IMcpToolFactory` is only for loading all MCP tools for the agent.
- If you introduce `IChatAgentService`, register `AgentService` as its implementation: `builder.Services.AddScoped<IChatAgentService, AgentService>()` and optionally update UI to inject `IChatAgentService` for testability.
