# DDD Core + Infrastructure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split the single Blazor app into Core (domain + repository interface), Infrastructure (EF Core + repository implementation), and Host (Blazor), and replace direct DB access with the repository pattern so future hosts (e.g. Cron) can reuse Core.

**Architecture:** Core class library holds entities, shared models (AssistantSegment, ChatBubble, etc.), and `IConversationRepository`; no EF or Blazor. Infrastructure class library holds DbContext, migrations, `ConversationRepository` implementation, and Backfill. Host references Core and Infrastructure and composes DI; no direct `AppDbContext` in app services.

**Tech Stack:** .NET 10, EF Core 10, SQLite, Blazor Server, MudBlazor. Solution format: .slnx.

---

## Task 1: Create SmallEBot.Core project and add to solution

**Files:**
- Create: `SmallEBot.Core/SmallEBot.Core.csproj`
- Modify: `SmallEBot.slnx`

**Step 1: Create Core project file**

Create `SmallEBot.Core/SmallEBot.Core.csproj` with minimal class library (no EF, no Blazor):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmallEBot.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

**Step 2: Add Core to solution**

Edit `SmallEBot.slnx`: add a second `<Project>` line so the solution contains both projects.

Before:
```xml
<Solution>
  <Project Path="SmallEBot/SmallEBot.csproj" />
</Solution>
```

After:
```xml
<Solution>
  <Project Path="SmallEBot.Core/SmallEBot.Core.csproj" />
  <Project Path="SmallEBot/SmallEBot.csproj" />
</Solution>
```

**Step 3: Build to verify**

Run: `dotnet build SmallEBot.slnx`  
Expected: Core builds; SmallEBot may still build (no ref to Core yet).

**Step 4: Commit**

```bash
git add SmallEBot.Core/SmallEBot.Core.csproj SmallEBot.slnx
git commit -m "chore: add SmallEBot.Core project to solution"
```

---

## Task 2: Move ICreateTime and entity types into Core

**Files:**
- Create: `SmallEBot.Core/Models/ICreateTime.cs`
- Create: `SmallEBot.Core/Entities/Conversation.cs`
- Create: `SmallEBot.Core/Entities/ConversationTurn.cs`
- Create: `SmallEBot.Core/Entities/ChatMessage.cs`
- Create: `SmallEBot.Core/Entities/ToolCall.cs`
- Create: `SmallEBot.Core/Entities/ThinkBlock.cs`

**Step 1: Add ICreateTime in Core**

Create `SmallEBot.Core/Models/ICreateTime.cs`:

```csharp
namespace SmallEBot.Core.Models;

/// <summary>Marks an entity that has a creation time for timeline ordering.</summary>
public interface ICreateTime
{
    DateTime CreatedAt { get; }
}
```

**Step 2: Add Conversation and ConversationTurn (no cross-ref to other entities beyond Conversation)**

Create `SmallEBot.Core/Entities/Conversation.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace SmallEBot.Core.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    [MaxLength(20)]
    public string UserName { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public ICollection<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
    public ICollection<ThinkBlock> ThinkBlocks { get; set; } = new List<ThinkBlock>();
    public ICollection<ConversationTurn> Turns { get; set; } = new List<ConversationTurn>();
}
```

Create `SmallEBot.Core/Entities/ConversationTurn.cs`:

```csharp
namespace SmallEBot.Core.Entities;

public class ConversationTurn
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public bool IsThinkingMode { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
```

**Step 3: Add ChatMessage, ToolCall, ThinkBlock (reference Core.Models)**

Create `SmallEBot.Core/Entities/ChatMessage.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ChatMessage : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
```

Create `SmallEBot.Core/Entities/ToolCall.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ToolCall : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(200)]
    public string ToolName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
```

Create `SmallEBot.Core/Entities/ThinkBlock.cs`:

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ThinkBlock : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
```

**Step 4: Build Core**

Run: `dotnet build SmallEBot.Core/SmallEBot.Core.csproj`  
Expected: PASS.

**Step 5: Commit**

```bash
git add SmallEBot.Core/Models/ICreateTime.cs SmallEBot.Core/Entities/*.cs
git commit -m "feat(Core): add entities and ICreateTime"
```

---

## Task 3: Move conversation-related models into Core

**Files:**
- Create: `SmallEBot.Core/Models/AssistantSegment.cs`
- Create: `SmallEBot.Core/Models/StreamUpdate.cs`
- Create: `SmallEBot.Core/Models/TimelineItem.cs`
- Create: `SmallEBot.Core/Models/ChatBubble.cs`

**Step 1: Add AssistantSegment and StreamUpdate (no entity refs)**

Create `SmallEBot.Core/Models/AssistantSegment.cs`:

```csharp
namespace SmallEBot.Core.Models;

/// <summary>One segment of an assistant reply: text, think block, or tool call, in execution order.</summary>
public sealed record AssistantSegment(
    bool IsText,
    bool IsThink = false,
    string? Text = null,
    string? ToolName = null,
    string? Arguments = null,
    string? Result = null);
```

Create `SmallEBot.Core/Models/StreamUpdate.cs`:

```csharp
namespace SmallEBot.Core.Models;

public abstract record StreamUpdate;

public sealed record TextStreamUpdate(string Text) : StreamUpdate;

public sealed record ToolCallStreamUpdate(string ToolName, string? Arguments = null, string? Result = null) : StreamUpdate;

public sealed record ThinkStreamUpdate(string Text) : StreamUpdate;
```

**Step 2: Add TimelineItem and ChatBubble (reference Core.Entities)**

Create `SmallEBot.Core/Models/TimelineItem.cs`:

```csharp
using SmallEBot.Core.Entities;

namespace SmallEBot.Core.Models;

/// <summary>One entry in the conversation timeline (message, tool call, or think block), sorted by CreatedAt.</summary>
public sealed record TimelineItem(ChatMessage? Message, ToolCall? ToolCall, ThinkBlock? ThinkBlock)
{
    public DateTime CreatedAt => Message?.CreatedAt ?? ToolCall?.CreatedAt ?? ThinkBlock!.CreatedAt;
}
```

Create `SmallEBot.Core/Models/ChatBubble.cs`:

```csharp
using SmallEBot.Core.Entities;

namespace SmallEBot.Core.Models;

/// <summary>One conversation bubble: either a user bubble or an assistant bubble.</summary>
public abstract record ChatBubble;

/// <summary>User bubble containing a single user message.</summary>
public sealed record UserBubble(ChatMessage Message) : ChatBubble;

/// <summary>Assistant bubble containing one AI reply (text, tool calls, reasoning in order).</summary>
public sealed record AssistantBubble(IReadOnlyList<TimelineItem> Items, bool IsThinkingMode) : ChatBubble;
```

**Step 3: Build Core**

Run: `dotnet build SmallEBot.Core/SmallEBot.Core.csproj`  
Expected: PASS.

**Step 4: Commit**

```bash
git add SmallEBot.Core/Models/AssistantSegment.cs SmallEBot.Core/Models/StreamUpdate.cs SmallEBot.Core/Models/TimelineItem.cs SmallEBot.Core/Models/ChatBubble.cs
git commit -m "feat(Core): add conversation-related models"
```

---

## Task 4: Add IConversationRepository and ConversationBubbleHelper in Core

**Files:**
- Create: `SmallEBot.Core/Repositories/IConversationRepository.cs`
- Create: `SmallEBot.Core/ConversationBubbleHelper.cs`

**Step 1: Define IConversationRepository**

Create `SmallEBot.Core/Repositories/IConversationRepository.cs`:

```csharp
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default);
    Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default);
    Task<List<ChatMessage>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default);
    Task<Conversation> CreateAsync(string userName, string title, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default);
    Task<Guid> AddTurnAndUserMessageAsync(Guid conversationId, string userName, string userMessage, bool useThinking, string? newTitle, CancellationToken ct = default);
    Task CompleteTurnWithAssistantAsync(Guid conversationId, Guid turnId, IReadOnlyList<AssistantSegment> segments, CancellationToken ct = default);
    Task CompleteTurnWithErrorAsync(Guid conversationId, Guid turnId, string errorMessage, CancellationToken ct = default);
}
```

**Step 2: Add ConversationBubbleHelper (pure logic from ConversationService)**

Create `SmallEBot.Core/ConversationBubbleHelper.cs`:

```csharp
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;

namespace SmallEBot.Core;

/// <summary>Pure domain logic for building chat bubbles from a conversation aggregate.</summary>
public static class ConversationBubbleHelper
{
    /// <summary>Returns conversation timeline (messages, tool calls, think blocks) sorted by CreatedAt.</summary>
    public static List<TimelineItem> GetTimeline(IEnumerable<ChatMessage> messages, IEnumerable<ToolCall> toolCalls, IEnumerable<ThinkBlock> thinkBlocks)
    {
        var list = messages.Select(m => new TimelineItem(m, null, null))
            .Concat(toolCalls.Select(t => new TimelineItem(null, t, null)))
            .Concat(thinkBlocks.Select(b => new TimelineItem(null, null, b)))
            .OrderBy(x => x.CreatedAt)
            .ToList();
        return list;
    }

    /// <summary>Returns conversation as chat bubbles from turns: one bubble = one user message or one assistant reply.</summary>
    public static List<ChatBubble> GetChatBubbles(Conversation conv)
    {
        var bubbles = new List<ChatBubble>();
        var turns = conv.Turns.OrderBy(t => t.CreatedAt).ToList();
        if (turns.Count == 0)
        {
            var timeline = GetTimeline(conv.Messages, conv.ToolCalls, conv.ThinkBlocks);
            var currentAssistant = new List<TimelineItem>();
            foreach (var item in timeline)
            {
                if (item.Message is { Role: "user" })
                {
                    if (currentAssistant.Count > 0)
                        bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false));
                    bubbles.Add(new UserBubble(item.Message));
                    currentAssistant = [];
                }
                else
                    currentAssistant.Add(item);
            }
            if (currentAssistant.Count > 0)
                bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false));
            return bubbles;
        }

        foreach (var turn in turns)
        {
            var userMsg = conv.Messages.FirstOrDefault(m => m.TurnId == turn.Id && m.Role == "user");
            if (userMsg == null) continue;

            var turnMessages = conv.Messages.Where(m => m.TurnId == turn.Id && m.Role == "assistant").ToList();
            var turnTools = conv.ToolCalls.Where(t => t.TurnId == turn.Id).ToList();
            var turnThinks = conv.ThinkBlocks.Where(b => b.TurnId == turn.Id).ToList();
            var items = GetTimeline(turnMessages, turnTools, turnThinks);

            bubbles.Add(new UserBubble(userMsg));
            bubbles.Add(new AssistantBubble(items, turn.IsThinkingMode));
        }
        return bubbles;
    }
}
```

**Step 3: Build Core**

Run: `dotnet build SmallEBot.Core/SmallEBot.Core.csproj`  
Expected: PASS.

**Step 4: Commit**

```bash
git add SmallEBot.Core/Repositories/IConversationRepository.cs SmallEBot.Core/ConversationBubbleHelper.cs
git commit -m "feat(Core): add IConversationRepository and ConversationBubbleHelper"
```

---

## Task 5: Create SmallEBot.Infrastructure project and add to solution

**Files:**
- Create: `SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`
- Modify: `SmallEBot.slnx`

**Step 1: Create Infrastructure project**

Create `SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmallEBot.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.3">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../SmallEBot.Core/SmallEBot.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Add Infrastructure to solution**

Edit `SmallEBot.slnx` so it has three projects (Core, Infrastructure, SmallEBot):

```xml
<Solution>
  <Project Path="SmallEBot.Core/SmallEBot.Core.csproj" />
  <Project Path="SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj" />
  <Project Path="SmallEBot/SmallEBot.csproj" />
</Solution>
```

**Step 3: Build**

Run: `dotnet build SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`  
Expected: PASS (Infrastructure builds; Core is referenced).

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj SmallEBot.slnx
git commit -m "chore: add SmallEBot.Infrastructure project"
```

---

## Task 6: Move DbContext and migrations into Infrastructure

**Files:**
- Create: `SmallEBot.Infrastructure/Data/SmallEBotDbContext.cs` (copy from SmallEBot/Data/AppDbContext.cs, update namespace and type name to SmallEBot.Infrastructure.Data, reference Core entities)
- Create: All migration files under `SmallEBot.Infrastructure/Data/Migrations/` (copy from SmallEBot/Data/Migrations/, update namespace to SmallEBot.Infrastructure.Data.Migrations and DbContext type to SmallEBotDbContext)

**Step 1: Add SmallEBotDbContext in Infrastructure**

Create `SmallEBot.Infrastructure/Data/SmallEBotDbContext.cs`. Use the same configuration as current `AppDbContext` but:
- Namespace: `SmallEBot.Infrastructure.Data`
- Class name: `SmallEBotDbContext`
- `using SmallEBot.Core.Entities;` for entity types (Conversation, ConversationTurn, ChatMessage, ToolCall, ThinkBlock).

Copy the full `OnModelCreating` and `DbSet<>` definitions from `SmallEBot/Data/AppDbContext.cs`, changing only namespace and class name.

**Step 2: Copy migrations and fix namespaces**

Copy every file from `SmallEBot/Data/Migrations/` to `SmallEBot.Infrastructure/Data/Migrations/`. In each file:
- Replace `namespace SmallEBot.Data.Migrations` with `namespace SmallEBot.Infrastructure.Data.Migrations`
- Replace `SmallEBot.Data` (usings) with `SmallEBot.Infrastructure.Data`
- Replace `AppDbContext` with `SmallEBotDbContext` (class name and type references)
- In Designer files, ensure `[DbContext(typeof(SmallEBotDbContext))]` and model builder uses `SmallEBotDbContext`
- Update entity type namespaces from `SmallEBot.Data.Entities` to `SmallEBot.Core.Entities` in migration/Designer files where they appear

**Step 3: Build Infrastructure**

Run: `dotnet build SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`  
Expected: PASS.

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Data/
git commit -m "feat(Infrastructure): add SmallEBotDbContext and migrations"
```

---

## Task 7: Implement ConversationRepository in Infrastructure

**Files:**
- Create: `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`

**Step 1: Implement ConversationRepository**

Create `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs` that:
- Implements `SmallEBot.Core.Repositories.IConversationRepository`
- Takes `SmallEBot.Infrastructure.Data.SmallEBotDbContext` in constructor
- Implements each method by porting logic from current `ConversationService` and `AgentService` (CreateAsync, GetByIdAsync, GetListAsync, GetMessagesForConversationAsync, GetMessageCountAsync, DeleteAsync, AddTurnAndUserMessageAsync, CompleteTurnWithAssistantAsync, CompleteTurnWithErrorAsync). Use `SmallEBot.Core.Entities` and `SmallEBot.Core.Models` types. Use `AsSplitQuery()`, `Include()`, and `OrderBy` as in current code.

**Step 2: Build**

Run: `dotnet build SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`  
Expected: PASS.

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Repositories/ConversationRepository.cs
git commit -m "feat(Infrastructure): implement ConversationRepository"
```

---

## Task 8: Add BackfillTurns in Infrastructure

**Files:**
- Create: `SmallEBot.Infrastructure/Data/BackfillTurnsService.cs` (or static helper that takes DbContext)

**Step 1: Port BackfillTurnsAsync**

Create a class or static helper in Infrastructure that performs the same logic as current `ConversationService.BackfillTurnsAsync`: load conversations with messages/tool calls/think blocks, create turns and assign TurnId, save. It should use `SmallEBotDbContext` (injected or passed). Do not expose this in Core; it is an Infrastructure one-off migration helper.

**Step 2: Build**

Run: `dotnet build SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`  
Expected: PASS.

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Data/BackfillTurnsService.cs
git commit -m "feat(Infrastructure): add BackfillTurns for existing data"
```

---

## Task 9: Host references Core and Infrastructure; register DbContext and repository

**Files:**
- Modify: `SmallEBot/SmallEBot.csproj` (add ProjectReference to Core and Infrastructure)
- Modify: `SmallEBot/Program.cs` (register DbContext from Infrastructure, register IConversationRepository → ConversationRepository; run Backfill after Migrate)

**Step 1: Add project references to Host**

Edit `SmallEBot/SmallEBot.csproj`: add inside `<ItemGroup>`:

```xml
<ProjectReference Include="../SmallEBot.Core/SmallEBot.Core.csproj" />
<ProjectReference Include="../SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj" />
```

**Step 2: Register Infrastructure in Program.cs**

In `Program.cs`:
- Add `using SmallEBot.Infrastructure.Data;` and `using SmallEBot.Infrastructure.Repositories;` and `using SmallEBot.Core.Repositories;`
- Replace `AddDbContext<AppDbContext>` with `AddDbContext<SmallEBotDbContext>(...)` (same options; connection string unchanged)
- Add `builder.Services.AddScoped<IConversationRepository, ConversationRepository>();`
- Replace `GetRequiredService<AppDbContext>` with `GetRequiredService<SmallEBotDbContext>` for `Migrate()`
- Replace Backfill call: get `IConversationRepository` or a dedicated Backfill service from Infrastructure (if Backfill is a service, register it and call it after Migrate; if static, resolve DbContext and call static method). Design: Backfill can be a scoped service that takes SmallEBotDbContext and implements a single `Task RunAsync(CancellationToken ct)` method; register it in Program and call after Migrate.

**Step 3: Build solution**

Run: `dotnet build SmallEBot.slnx`  
Expected: PASS (all three projects).

**Step 4: Commit**

```bash
git add SmallEBot/SmallEBot.csproj SmallEBot/Program.cs
git commit -m "feat(Host): reference Core and Infrastructure, register DbContext and repository"
```

---

## Task 10: Refactor ConversationService in Host to use IConversationRepository and Core helper

**Files:**
- Modify: `SmallEBot/Services/ConversationService.cs`
- Modify: All call sites that use `ConversationService` (ensure they still work; ChatPage, ConversationSidebar, etc. use ConversationService for list, get by id, delete, create; and GetChatBubbles)

**Step 1: Refactor ConversationService**

- Change `ConversationService` constructor to take `IConversationRepository` instead of `AppDbContext`.
- Implement each method by delegating to `IConversationRepository`: CreateAsync, GetListAsync, GetByIdAsync, GetMessageCountAsync, DeleteAsync. Remove all direct DB access.
- Replace the static `GetChatBubbles(Conversation conv)` and the private `GetTimeline` with calls to `ConversationBubbleHelper.GetChatBubbles(conv)` and remove the in-class implementation. Add `using SmallEBot.Core;`.
- Remove `BackfillTurnsAsync` from ConversationService (moved to Infrastructure); callers no longer call it from ConversationService (Program.cs calls Backfill from Infrastructure).

**Step 2: Update usings in ConversationService**

Use `SmallEBot.Core.Entities`, `SmallEBot.Core.Models`, `SmallEBot.Core.Repositories`, `SmallEBot.Core` for Conversation, ChatBubble, IConversationRepository, ConversationBubbleHelper.

**Step 3: Build and run**

Run: `dotnet build SmallEBot.slnx`  
Then: `dotnet run --project SmallEBot` and verify: create conversation, send message, list conversations, open conversation, delete conversation.  
Expected: Build PASS; app runs and conversation flows work.

**Step 4: Commit**

```bash
git add SmallEBot/Services/ConversationService.cs
git commit -m "refactor(Host): ConversationService uses IConversationRepository and ConversationBubbleHelper"
```

---

## Task 11: Refactor AgentService to use IConversationRepository and remove ChatMessageStoreAdapter

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Delete: `SmallEBot/Services/ChatMessageStoreAdapter.cs`

**Step 1: Change AgentService to depend on IConversationRepository**

- Constructor: replace `AppDbContext db` with `IConversationRepository conversationRepository`.
- `GetEstimatedContextUsageAsync`: replace `new ChatMessageStoreAdapter(db, conversationId)` and `store.LoadMessagesAsync(ct)` with `conversationRepository.GetMessagesForConversationAsync(conversationId, ct)`.
- `SendMessageStreamingAsync`: same replacement for loading history.
- `CreateTurnAndUserMessageAsync`: remove all direct `db.Conversations`, `db.ConversationTurns`, `db.ChatMessages` usage; call `conversationRepository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, ct)`. Title generation: keep logic in AgentService but pass the new title via the repository method (repository returns turnId; title can be updated inside AddTurnAndUserMessageAsync when newTitle is non-null).
- `CompleteTurnWithAssistantAsync` and `CompleteTurnWithErrorAsync`: replace direct db writes with `conversationRepository.CompleteTurnWithAssistantAsync(...)` and `conversationRepository.CompleteTurnWithErrorAsync(...)`.

**Step 2: Remove ChatMessageStoreAdapter**

Delete `SmallEBot/Services/ChatMessageStoreAdapter.cs`. Ensure no other file references it (grep for ChatMessageStoreAdapter).

**Step 3: Update usings in AgentService**

Use `SmallEBot.Core.Entities`, `SmallEBot.Core.Models`, `SmallEBot.Core.Repositories`; remove `SmallEBot.Data` and `SmallEBot.Data.Entities` for DB types. Keep `Microsoft.Extensions.AI` and existing model types that are now in Core (StreamUpdate, AssistantSegment from Core.Models).

**Step 4: Build and run**

Run: `dotnet build SmallEBot.slnx`; then `dotnet run --project SmallEBot`. Verify: send message, stream reply, context usage display.  
Expected: PASS.

**Step 5: Commit**

```bash
git add SmallEBot/Services/AgentService.cs
git rm SmallEBot/Services/ChatMessageStoreAdapter.cs
git commit -m "refactor(Host): AgentService uses IConversationRepository; remove ChatMessageStoreAdapter"
```

---

## Task 12: Point Host UI and components to Core types

**Files:**
- Modify: All Razor and .cs files in SmallEBot that reference `SmallEBot.Data.Entities` or `SmallEBot.Models` for conversation/agent types to use `SmallEBot.Core.Entities` or `SmallEBot.Core.Models` instead.

**Step 1: Update usings globally**

- `Components/Pages/ChatPage.razor`: @using SmallEBot.Data.Entities → @using SmallEBot.Core.Entities (and Core.Models if needed)
- `Components/Chat/ConversationSidebar.razor`: same
- `Components/Chat/ChatArea.razor` and `ChatArea.razor.cs`: SmallEBot.Models → SmallEBot.Core.Models (for ChatBubble, AssistantSegment, etc.)
- Any other file that uses Conversation, ChatMessage, ChatBubble, StreamUpdate, AssistantSegment, TimelineItem: switch to Core namespaces.

**Step 2: Build**

Run: `dotnet build SmallEBot.slnx`  
Expected: PASS.

**Step 3: Commit**

```bash
git add SmallEBot/Components/
git commit -m "refactor(Host): use Core namespaces in components"
```

---

## Task 13: Remove duplicate entities and models from Host

**Files:**
- Delete: `SmallEBot/Data/Entities/Conversation.cs`, `ConversationTurn.cs`, `ChatMessage.cs`, `ToolCall.cs`, `ThinkBlock.cs`
- Delete: `SmallEBot/Data/AppDbContext.cs`
- Delete: entire `SmallEBot/Data/Migrations/` folder
- Delete: `SmallEBot/Models/ICreateTime.cs`, `AssistantSegment.cs`, `StreamUpdate.cs`, `ChatBubble.cs`, `TimelineItem.cs`
- Keep in Host: `SmallEBot/Models/McpServerEntry.cs`, `SkillMetadata.cs`, `SmallEBotSettings.cs` (config/UI models not moved to Core in this plan)

**Step 1: Delete moved files**

Remove the listed files and the Data folder (except if any host-specific code still lives there; it should not after Task 11). Do not remove `SmallEBot/Models/` folder if McpServerEntry, SkillMetadata, SmallEBotSettings remain.

**Step 2: Build**

Run: `dotnet build SmallEBot.slnx`  
Expected: PASS (Host now gets Conversation, ChatMessage, etc. from Core; DbContext and migrations only in Infrastructure).

**Step 3: Commit**

```bash
git rm SmallEBot/Data/Entities/*.cs SmallEBot/Data/AppDbContext.cs
git rm -r SmallEBot/Data/Migrations
git rm SmallEBot/Models/ICreateTime.cs SmallEBot/Models/AssistantSegment.cs SmallEBot/Models/StreamUpdate.cs SmallEBot/Models/ChatBubble.cs SmallEBot/Models/TimelineItem.cs
git commit -m "chore: remove duplicate entities and models from Host (now in Core)"
```

---

## Task 14: Fix remaining Host references and EF migrations command

**Files:**
- Modify: Any file that still references `SmallEBot.Data` or `SmallEBot.Data.Entities` or old SmallEBot.Models types (grep and fix)
- Modify: `AGENTS.md` (update build/run/migrations commands and architecture description)

**Step 1: Grep and fix remaining references**

Run: `rg "SmallEBot\.Data|SmallEBot\.Models" SmallEBot/ --type cs -l` and `rg "SmallEBot\.Data|SmallEBot\.Models" SmallEBot/ --glob "*.razor" -l`. For conversation/entity types, use Core. For McpServerEntry, SkillMetadata, SmallEBotSettings keep SmallEBot.Models (Host).

**Step 2: Update AGENTS.md**

- Build: `dotnet build` or `dotnet build SmallEBot/SmallEBot.csproj` (unchanged)
- Run: `dotnet run --project SmallEBot` (unchanged)
- EF migrations: `dotnet ef migrations add <MigrationName> --project SmallEBot.Infrastructure --startup-project SmallEBot`
- Architecture: mention Core (domain + repository interface), Infrastructure (EF + repository impl), Host (Blazor); request flow and Agent/Skills/MCP description updated so that data access goes through IConversationRepository.

**Step 3: Build and run**

Run: `dotnet build SmallEBot.slnx` and `dotnet run --project SmallEBot`.  
Expected: PASS; app runs; create/send/delete conversation works.

**Step 4: Commit**

```bash
git add SmallEBot/ AGENTS.md
git commit -m "docs: update AGENTS.md for Core/Infrastructure layout; fix remaining refs"
```

---

## Task 15: Verify migrations from Infrastructure and optional cleanup

**Files:**
- Modify: None unless issues found

**Step 1: Verify migration command**

Run: `dotnet ef migrations list --project SmallEBot.Infrastructure --startup-project SmallEBot`  
Expected: List of existing migrations (no new migration needed unless schema change).

**Step 2: Full verification**

- Build: `dotnet build SmallEBot.slnx`
- Run: `dotnet run --project SmallEBot`
- Manually: create conversation, send message, view history, delete conversation, open sidebar, switch theme/settings if applicable.  
Expected: All flows work; no runtime errors.

**Step 3: Commit (if any small fixes)**

If any fix was made in this task, commit with a short message. Otherwise, no commit.

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-16-ddd-core-infrastructure-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** – One subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** – Open a new session with executing-plans and run in a worktree with checkpoint reviews.

Which approach do you want?
