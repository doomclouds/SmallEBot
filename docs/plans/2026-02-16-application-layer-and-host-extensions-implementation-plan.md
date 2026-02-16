# Application layer and Host DI extensions — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add SmallEBot.Application with the reusable conversation pipeline (IAgentConversationService, IAgentRunner, IStreamSink), move Host service registration into an Extensions class, and keep Host as a thin composition root.

**Architecture:** Application references only Core; defines IAgentRunner and IStreamSink and implements IAgentConversationService (create turn → run agent via IAgentRunner → push updates to IStreamSink → persist segments). Host implements IAgentRunner (using IAgentBuilder) and IStreamSink (e.g. channel-based for Blazor), and registers everything via `AddSmallEBotHostServices(IServiceCollection, IConfiguration)`.

**Tech Stack:** .NET 10, Blazor Server, existing Core/Infrastructure. Use **development branch** and **worktree** (`.worktrees/feature-application-layer-extensions`).

---

## Task 1: Create feature branch and worktree

**Prerequisite:** `.worktrees/` is in `.gitignore` (already present).

**Step 1: Create worktree with new branch**

From repo root (e.g. `d:\RiderProjects\SmallEBot`):

```powershell
git worktree add ".worktrees/feature-application-layer-extensions" -b feature/application-layer-extensions
```

**Step 2: Switch context to worktree**

All following tasks run inside the worktree:

```powershell
cd "d:\RiderProjects\SmallEBot\.worktrees\feature-application-layer-extensions"
```

**Step 3: Verify clean build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Build succeeds.

**Step 4: Commit (no code change yet)**

Optional: if you need to record the branch creation, commit can be done after Task 2 when first code changes exist. Otherwise proceed to Task 2.

---

## Task 2: Add SmallEBot.Application project and solution reference

**Files:**
- Create: `SmallEBot.Application/SmallEBot.Application.csproj`
- Modify: `SmallEBot.slnx`

**Step 1: Create Application project file**

Create `SmallEBot.Application/SmallEBot.Application.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmallEBot.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../SmallEBot.Core/SmallEBot.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Add Application to solution**

Edit `SmallEBot.slnx`: insert Application **after** Core, **before** Infrastructure:

```xml
<Solution>
  <Project Path="SmallEBot.Core/SmallEBot.Core.csproj" />
  <Project Path="SmallEBot.Application/SmallEBot.Application.csproj" />
  <Project Path="SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj" />
  <Project Path="SmallEBot/SmallEBot.csproj" />
</Solution>
```

**Step 3: Add Host → Application reference**

Edit `SmallEBot/SmallEBot.csproj`: add:

```xml
<ProjectReference Include="../SmallEBot.Application/SmallEBot.Application.csproj" />
```

(Keep existing Core and Infrastructure references.)

**Step 4: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: All projects build.

**Step 5: Commit**

```powershell
git add SmallEBot.Application/SmallEBot.Application.csproj SmallEBot.slnx SmallEBot/SmallEBot.csproj
git commit -m "chore: add SmallEBot.Application project and Host reference"
```

---

## Task 3: Define IStreamSink, IAgentRunner, and IAgentConversationService in Application

**Files:**
- Create: `SmallEBot.Application/Streaming/IStreamSink.cs`
- Create: `SmallEBot.Application/Streaming/IAgentRunner.cs`
- Create: `SmallEBot.Application/Conversation/IAgentConversationService.cs`

**Step 1: Add IStreamSink**

Create `SmallEBot.Application/Streaming/IStreamSink.cs`:

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Streaming;

/// <summary>Receives streamed updates from the agent (text, think, tool call). Implemented by the host (e.g. Blazor writes to a channel, Cron no-ops).</summary>
public interface IStreamSink
{
    ValueTask OnNextAsync(StreamUpdate update, CancellationToken cancellationToken = default);
}
```

**Step 2: Add IAgentRunner**

Create `SmallEBot.Application/Streaming/IAgentRunner.cs`:

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Streaming;

/// <summary>Runs the agent and yields stream updates. Implemented by the host (uses IAgentBuilder, MCP, etc.).</summary>
public interface IAgentRunner
{
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default);

    /// <summary>Generate a short title for a conversation from its first message. Used when message count is 0.</summary>
    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);
}
```

**Step 3: Add IAgentConversationService**

Create `SmallEBot.Application/Conversation/IAgentConversationService.cs`:

```csharp
using SmallEBot.Core.Entities;

namespace SmallEBot.Application.Conversation;

/// <summary>Orchestrates conversation CRUD and the send-message-and-stream pipeline. Implemented in Application; consumed by Host.</summary>
public interface IAgentConversationService
{
    Task<Conversation> CreateConversationAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<Conversation>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    Task<Conversation?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Creates a turn and user message; returns turn id. Call before StreamResponseAndCompleteAsync.</summary>
    Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default);

    /// <summary>Streams agent reply to the sink and persists assistant segments. Call after CreateTurnAndUserMessageAsync.</summary>
    Task StreamResponseAndCompleteAsync(
        Guid conversationId,
        Guid turnId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default);

    /// <summary>Persist assistant segments for an existing turn (e.g. on success).</summary>
    Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<Core.Models.AssistantSegment> segments,
        CancellationToken cancellationToken = default);

    /// <summary>Persist error as assistant reply for the turn.</summary>
    Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
```

**Step 4: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Application and Host build (Application has no implementation yet; Host will fail when we wire it later if interfaces are not registered).

**Step 5: Commit**

```powershell
git add SmallEBot.Application/Streaming/IStreamSink.cs SmallEBot.Application/Streaming/IAgentRunner.cs SmallEBot.Application/Conversation/IAgentConversationService.cs
git commit -m "feat(Application): add IStreamSink, IAgentRunner, IAgentConversationService"
```

---

## Task 4: Implement StreamUpdate-to-AssistantSegment conversion and AgentConversationService

**Files:**
- Create: `SmallEBot.Application/Conversation/StreamUpdateToSegments.cs` (helper to convert stream updates to segments for persistence)
- Create: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Add StreamUpdateToSegments helper**

Application must accumulate `StreamUpdate` and convert to `List<AssistantSegment>` for `CompleteTurnWithAssistantAsync`. Create a static helper that mirrors the Host ChatArea segment-building logic (reasoning blocks as think + tool steps, then reply text/tool).

Create `SmallEBot.Application/Conversation/StreamUpdateToSegments.cs`:

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Conversation;

/// <summary>Converts a sequence of stream updates into ordered AssistantSegments for persistence (same semantics as ChatArea segment building).</summary>
public static class StreamUpdateToSegments
{
    public static List<AssistantSegment> ToSegments(IReadOnlyList<StreamUpdate> updates, bool useThinking)
    {
        var segments = new List<AssistantSegment>();
        string? textBuffer = null;
        foreach (var u in updates)
        {
            switch (u)
            {
                case ThinkStreamUpdate think when useThinking:
                    FlushText(ref textBuffer, segments);
                    segments.Add(new AssistantSegment(false, true, think.Text));
                    break;
                case ToolCallStreamUpdate tool when useThinking:
                    FlushText(ref textBuffer, segments);
                    segments.Add(new AssistantSegment(false, false, null, tool.ToolName, tool.Arguments, tool.Result));
                    break;
                case TextStreamUpdate text:
                    textBuffer = (textBuffer ?? "") + text.Text;
                    break;
                case ToolCallStreamUpdate tool when !useThinking:
                    FlushText(ref textBuffer, segments);
                    segments.Add(new AssistantSegment(false, false, null, tool.ToolName, tool.Arguments, tool.Result));
                    break;
            }
        }
        FlushText(ref textBuffer, segments);
        return segments;
    }

    private static void FlushText(ref string? buffer, List<AssistantSegment> segments)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        segments.Add(new AssistantSegment(true, false, buffer));
        buffer = null;
    }
}
```

**Step 2: Implement AgentConversationService**

Create `SmallEBot.Application/Conversation/AgentConversationService.cs`:

- Dependencies: `IConversationRepository`, `IAgentRunner`, inject via constructor.
- `CreateConversationAsync` → `repository.CreateAsync(userName, "新对话", ct)`.
- `GetConversationsAsync` → `repository.GetListAsync(userName, ct)`.
- `GetConversationAsync` → `repository.GetByIdAsync(id, userName, ct)`.
- `DeleteConversationAsync` → `repository.DeleteAsync(id, userName, ct)`.
- `GetMessageCountAsync` → `repository.GetMessageCountAsync(conversationId, ct)`.
- `CreateTurnAndUserMessageAsync`: (1) get message count; if 0, call `IAgentRunner.GenerateTitleAsync(userMessage, ct)` and pass as `newTitle`, else `newTitle` null; (2) return `repository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, ct)`.
- `StreamResponseAndCompleteAsync`: (1) create a list `var updates = new List<StreamUpdate>()`; (2) `await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, ct))` then `updates.Add(update)` and `await sink.OnNextAsync(update, ct)`; (3) after loop, `var segments = StreamUpdateToSegments.ToSegments(updates, useThinking)`; (4) `await repository.CompleteTurnWithAssistantAsync(conversationId, turnId, segments, ct)`.
- `CompleteTurnWithAssistantAsync` → `repository.CompleteTurnWithAssistantAsync(...)`.
- `CompleteTurnWithErrorAsync` → `repository.CompleteTurnWithErrorAsync(...)`.

Use `SmallEBot.Core.Repositories`, `SmallEBot.Core.Entities`, `SmallEBot.Core.Models`, and `SmallEBot.Application.Streaming` namespaces.

**Step 3: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Application builds. Host may still have old AgentService/ConversationService; we will wire Application in the next tasks.

**Step 4: Commit**

```powershell
git add SmallEBot.Application/Conversation/StreamUpdateToSegments.cs SmallEBot.Application/Conversation/AgentConversationService.cs
git commit -m "feat(Application): implement StreamUpdateToSegments and AgentConversationService"
```

---

## Task 5: Host — implement IAgentRunner (AgentRunnerAdapter)

**Files:**
- Create: `SmallEBot/Services/AgentRunnerAdapter.cs`

**Step 1: Implement AgentRunnerAdapter**

- Implements `SmallEBot.Application.Streaming.IAgentRunner`.
- Constructor: inject `IAgentBuilder` (existing Host service).
- `RunStreamingAsync`: call `agentBuilder.GetOrCreateAgentAsync(useThinking, ct)`; load history via… but Application does not inject repository into the runner. So the runner needs conversation history to build the message list. So either (A) IAgentRunner gets `IConversationRepository` injected in the Host adapter and loads messages inside `RunStreamingAsync`, or (B) Application passes the message list into the runner. Design says "given conversation and message" — so the runner receives conversationId and userMessage. So the Host’s AgentRunnerAdapter must load history from repository. So inject `IConversationRepository` and `IAgentBuilder` into AgentRunnerAdapter. In `RunStreamingAsync`: get messages with `conversationRepository.GetMessagesForConversationAsync(conversationId, ct)`; build framework messages (same as current AgentService: ToChatRole, add user message); get agent and `await foreach (var update in agent.RunStreamingAsync(...))` and map to `StreamUpdate` (TextStreamUpdate, ThinkStreamUpdate, ToolCallStreamUpdate) and yield. For `GenerateTitleAsync`: same as current AgentService.GenerateTitleAsync (get agent, run short prompt, trim to 20 chars).
- Use `Microsoft.Extensions.AI` for ChatMessage/ChatRole; use `SmallEBot.Core.Entities` for message role/content mapping; use `SmallEBot.Core.Models` for StreamUpdate subtypes.

**Step 2: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Build succeeds. Do not register in DI yet (Task 7).

**Step 3: Commit**

```powershell
git add SmallEBot/Services/AgentRunnerAdapter.cs
git commit -m "feat(Host): add AgentRunnerAdapter implementing IAgentRunner"
```

---

## Task 6: Host — implement IStreamSink (channel-based for Blazor)

**Files:**
- Create: `SmallEBot/Services/ChannelStreamSink.cs`

**Step 1: Implement ChannelStreamSink**

- Implements `SmallEBot.Application.Streaming.IStreamSink`.
- Holds a `ChannelWriter<StreamUpdate>` (from `System.Threading.Channels`). Constructor: `ChannelStreamSink(ChannelWriter<StreamUpdate> writer)`. `OnNextAsync(StreamUpdate update, CancellationToken ct)`: `await writer.WriteAsync(update, ct)`.
- Optionally: implement `IDisposable` / `IAsyncDisposable` and complete the writer on dispose (or let the caller complete). Simplest: no dispose; caller creates channel and completes writer when done.
- Use `SmallEBot.Core.Models` for `StreamUpdate`.

**Step 2: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Build succeeds.

**Step 3: Commit**

```powershell
git add SmallEBot/Services/ChannelStreamSink.cs
git commit -m "feat(Host): add ChannelStreamSink implementing IStreamSink"
```

---

## Task 7: Wire Application in Host — ConversationService facade and ChatArea pipeline

**Files:**
- Modify: `SmallEBot/Program.cs` (temporary: add registrations for Application + adapters; will move to Extensions in Task 8)
- Modify: `SmallEBot/Services/ConversationService.cs` (delegate to IAgentConversationService for CRUD)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` and `ChatArea.razor.cs` (use IAgentConversationService + channel + sink for send-message flow)
- Modify: `SmallEBot/Services/AgentService.cs` (keep only InvalidateAgentAsync and GetEstimatedContextUsageAsync; remove SendMessageStreamingAsync, CreateTurnAndUserMessageAsync, CompleteTurn* — those are now on IAgentConversationService)

**Step 1: Register Application and adapters in Program.cs**

Add after existing registrations:

```csharp
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Streaming;

// Application
builder.Services.AddScoped<IAgentConversationService, AgentConversationService>();
builder.Services.AddScoped<IAgentRunner, AgentRunnerAdapter>();
// IStreamSink is created per-request in ChatArea (channel-based), not registered in DI
```

Ensure `AgentConversationService` and `AgentRunnerAdapter` are in scope (they depend on scoped services).

**Step 2: Refactor ConversationService to facade**

- Inject `IAgentConversationService` instead of (or in addition to) `IConversationRepository`. Delegate: `CreateAsync` → `CreateConversationAsync`, `GetListAsync` → `GetConversationsAsync`, `GetByIdAsync` → `GetConversationAsync`, `DeleteAsync` → `DeleteConversationAsync`, `GetMessageCountAsync` → `GetMessageCountAsync`. Keep `GetChatBubbles(Conversation conv)` as `ConversationBubbleHelper.GetChatBubbles(conv)` (static). Add `using SmallEBot.Application.Conversation` and `SmallEBot.Core` for ConversationBubbleHelper.

**Step 3: Refactor AgentService to thin facade**

- Remove: `SendMessageStreamingAsync`, `CreateTurnAndUserMessageAsync`, `CompleteTurnWithAssistantAsync`, `CompleteTurnWithErrorAsync`, and the private helpers used only by them (e.g. `ToJsonString`, `ToChatRole`, `GenerateTitleAsync`). Keep: constructor with `IConversationRepository`, `IAgentBuilder`, `ITokenizer`; `InvalidateAgentAsync`; `GetEstimatedContextUsageAsync`; `DisposeAsync`; and the token-count serialization helpers + request DTOs used by `GetEstimatedContextUsageAsync`. So AgentService no longer does turn creation or streaming — only invalidation and context % for the UI.

**Step 4: Refactor ChatArea to use IAgentConversationService and channel**

- Inject `IAgentConversationService` (e.g. `ConversationPipeline`) and keep `AgentService` for context % and invalidate. Flow: (1) turnId = await ConversationPipeline.CreateTurnAndUserMessageAsync(...); (2) var runTask = ConversationPipeline.StreamResponseAndCompleteAsync(conversationId, turnId, msg, useThinking, sink, ct); (3) await foreach (var update in channel.Reader.ReadAllAsync(ct)) and map to display; (4) await runTask; (5) on error/cancel call CompleteTurnWithErrorAsync(conversationId, turnId, message) and channel.Writer.Complete().

**Step 5: Build and run**

```powershell
dotnet build SmallEBot.slnx
dotnet run --project SmallEBot
```

Verify: Create conversation, send message, stream displays and persists. MCP/Skills dialogs still call AgentService.InvalidateAgentAsync.

**Step 6: Commit**

```powershell
git add SmallEBot/Program.cs SmallEBot/Services/ConversationService.cs SmallEBot/Services/AgentService.cs SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(Host): wire Application pipeline; ConversationService facade and ChatArea channel sink"
```

---

## Task 8: Add ServiceCollectionExtensions and slim Program.cs

**Files:**
- Create: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`
- Modify: `SmallEBot/Program.cs`

**Step 1: Create ServiceCollectionExtensions**

Create `SmallEBot/Extensions/ServiceCollectionExtensions.cs` with a single extension method:

- Method: `public static IServiceCollection AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)`.
- Move **all** service registrations from current `Program.cs` into this method: DbContext (connection string from config or base directory), `IConversationRepository`, `BackfillTurnsService`, `UserPreferencesService`, MCP services, Skills services, `IAgentContextFactory`, `IBuiltInToolFactory`, `IMcpToolFactory`, `IAgentBuilder`, Application’s `IAgentConversationService` and `IAgentRunner`, `ConversationService`, `AgentService`, `UserNameService`, `MarkdownService`, `ITokenizer` (DeepSeek/CharEstimate). Use the same lifetimes (Scoped/Singleton) as today. Build connection string from `AppDomain.CurrentDomain.BaseDirectory` + `"smallebot.db"` if not in config.
- Add `using Microsoft.EntityFrameworkCore;` and all Host/Application/Infrastructure/Core namespaces as needed.

**Step 2: Slim Program.cs**

- Remove all `builder.Services.Add*` and the `var baseDir` / `var dbPath` / `var connectionString` block.
- Add single line: `builder.Services.AddSmallEBotHostServices(builder.Configuration);`
- Keep: `AddMudServices()`, `AddRazorComponents().AddInteractiveServerComponents()`, app build, migrations/backfill scope, middleware, `MapStaticAssets`, `MapRazorComponents`, `app.Run()`.
- Add `using SmallEBot.Extensions;` if needed.

**Step 3: Build and run**

```powershell
dotnet build SmallEBot.slnx
dotnet run --project SmallEBot
```

Expected: Same behavior as before; no functional change.

**Step 4: Commit**

```powershell
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs SmallEBot/Program.cs
git commit -m "refactor(Host): move DI registration to ServiceCollectionExtensions"
```

---

## Task 9: Update AGENTS.md and verify

**Files:**
- Modify: `AGENTS.md`

**Step 1: Update Architecture section**

- In the **Architecture** / **Projects** bullet, add **SmallEBot.Application**: "Application (class library) — conversation pipeline (IAgentConversationService), IAgentRunner and IStreamSink interfaces; references Core only. Host implements IAgentRunner and IStreamSink, registers services via Extensions."
- Mention that Host service registration is done in `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (e.g. `AddSmallEBotHostServices`).

**Step 2: Build**

```powershell
dotnet build SmallEBot.slnx
```

Expected: Build succeeds.

**Step 3: Commit**

```powershell
git add AGENTS.md
git commit -m "docs: update AGENTS.md for Application layer and Host Extensions"
```