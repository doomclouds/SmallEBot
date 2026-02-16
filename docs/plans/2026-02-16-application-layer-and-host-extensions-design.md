# Application layer and Host DI extensions design

**Date:** 2026-02-16  
**Status:** Design validated. Prerequisite: [DDD Core + Infrastructure](2026-02-16-ddd-core-infrastructure-design.md) (done).

## 1. Goals

- **Reusable pipeline:** The full conversation flow (create/load conversation → send message → stream reply → persist) lives in a dedicated Application layer so Blazor, Cron, or CLI hosts can reuse it; each host only provides “how to build the agent” and “how to emit the stream.”
- **Clear Host registration:** Move all service registration from `Program.cs` into one or more `IServiceCollection` extension methods so the host’s composition is explicit and maintainable.

## 2. Architecture and project roles

**Reference chain:** SmallEBot (Host) → SmallEBot.Application → SmallEBot.Core. Host also → SmallEBot.Infrastructure → SmallEBot.Core. Application does **not** reference Infrastructure.

**SmallEBot.Core** (unchanged): Entities, `IConversationRepository`, shared models (e.g. `StreamUpdate`, `AssistantSegment`), and pure helpers (e.g. `ConversationBubbleHelper`). No use-case orchestration, no EF or host-specific types.

**SmallEBot.Application** (new): Holds the **conversation pipeline** use case. It defines and implements e.g. `IAgentConversationService` (or `IConversationPipeline`) with methods such as: create/load conversation by id, create turn, invoke an abstract “agent” for streaming, persist via repository and push stream chunks via an abstract sink. Application defines the interfaces it depends on, e.g. `IAgentRunner` (given conversation and message, runs the streaming call) and `IStreamSink` / `IStreamEmitter` (receives text/tool-call segments). Application does **not** implement agent building, MCP/Skills config, or env; the Host (or another host) implements these and injects them.

**SmallEBot (Host):** Implements `IAgentRunner` and `IStreamSink` (using existing `AgentBuilder`, `McpToolFactory`, etc.). Registers all services via an **extension method** (see below). Blazor/SignalR calls `IAgentConversationService` and wires `IStreamSink` to push to the client. Future hosts (Cron, CLI) reference Application + Core + Infrastructure and provide their own `IAgentRunner` and `IStreamSink` to reuse the same pipeline.

**SmallEBot.Infrastructure:** Unchanged; implements `IConversationRepository`, DbContext, migrations. No reference to Application.

## 3. Host service registration (Extensions)

**Current state:** Many services are registered directly in `Program.cs` (DbContext, repository, AgentBuilder, MCP, Skills, UserName, UserPreferences, Markdown, Tokenizer, etc.), which hurts readability and reuse.

**Approach:** Add an **extension class** in the Host that centralizes registration:

- **Class:** e.g. `ServiceCollectionExtensions` or `HostServiceExtensions`, under `SmallEBot/Extensions/` (or an agreed folder).
- **Method:** e.g. `AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)` so path/config can be read if needed.
- **Contents:** Register DbContext and connection string, `IConversationRepository`, Application’s `IAgentConversationService`, Host implementations of `IAgentRunner` and `IStreamSink`, MCP (McpConfigService, McpToolFactory, McpToolsLoaderService), Skills (SkillsConfigService, AgentContextFactory), BuiltInToolFactory, UserNameService, UserPreferencesService, MarkdownService, Tokenizer, ConversationService, AgentService (if still used by SignalR as a thin wrapper), and any other existing registrations. Optionally split into smaller extensions (e.g. `AddInfrastructure()`, `AddApplication()`, `AddHostAgentServices()`) and call them in order from one main extension.
- **Program.cs:** Becomes a short composition root, e.g. `builder.Services.AddSmallEBotHostServices(builder.Configuration);` plus MudBlazor, Razor, middleware, and app startup (migrations, backfill, `MapRazorComponents`, etc.).

This keeps “what the host wires” in one place and makes it easy to add or change registrations without touching `Program.cs` each time.

## 4. Key interfaces (Application layer)

- **IAgentConversationService** (in Application): Orchestrates the pipeline; e.g. `Task<Guid> CreateConversationAsync(...)`, `Task SendMessageAndStreamAsync(Guid conversationId, string userName, string message, IStreamSink sink, ...)`. Depends on `IConversationRepository` (Core) and `IAgentRunner`, `IStreamSink` (Application-defined, Host-implemented).
- **IAgentRunner** (in Application): Abstraction for “run the agent and stream.” Host implementation uses `AgentBuilder`, MCP, Skills, etc.; Cron/CLI would provide a different implementation (e.g. same builder with different config or a lighter runner).
- **IStreamSink** (in Application): Accepts streamed segments (text delta, tool call, thinking block, etc.). Host implementation pushes to SignalR; Cron might only persist or enqueue events.

Interfaces and DTOs used across Application and Host (e.g. segment types) stay in Core so Application does not depend on Host-specific types.

## 5. Summary

| Item | Location |
|------|----------|
| Conversation pipeline (create/send/stream/persist) | **Application** (IAgentConversationService implementation) |
| IAgentRunner, IStreamSink | **Application** (interfaces); **Host** (implementations) |
| All Host DI registration | **Host** `ServiceCollectionExtensions.AddSmallEBotHostServices(...)` |
| Program.cs | Minimal: call extension, MudBlazor, Razor, migrations/backfill, middleware, MapRazorComponents |

Implementation plan can follow: add Application project and interfaces → move pipeline from current AgentService/ConversationService into Application → add Host implementations of IAgentRunner/IStreamSink → add Extensions class and refactor Program.cs.
