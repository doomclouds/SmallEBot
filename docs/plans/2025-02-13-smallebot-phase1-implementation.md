# SmallEBot Phase 1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use **superpowers:subagent-driven-development** to execute this plan. Do **not** use executing-plans.

**Goal:** Implement a Blazor Server personal chat assistant with multi-conversation support, streaming responses, and Agent-generated titles using Microsoft.Agents.AI.OpenAI and DeepSeek.

**Architecture:** ASP.NET Core Blazor Server, SQLite + EF Core for persistence, Microsoft.Agents.AI.OpenAI for agent, MudBlazor for UI. UserName stored in ProtectedSessionStorage, first-visit username dialog.

**Tech Stack:** .NET 10, Blazor Server, Microsoft.Agents.AI.OpenAI (1.0.0-preview.260212.1), MudBlazor 8.15.0, EF Core 10.0.3, SQLite, DeepSeek (OpenAI-compatible).

**Reference:** `docs/plans/2026-02-13-smallebot-phase1-design.md`

**Note:** If .NET 10 SDK is not installed, use `net9.0` or `net8.0` when creating the project.

---

## Subagent-Driven Development Workflow (MUST follow)

**Prerequisite:** Run **superpowers:using-git-worktrees** to set up an isolated workspace before starting. Use the worktree as the implementation directory.

**Per task (never skip or reorder):**

1. **Implement:** Dispatch implementer with full task text + context. Implementer must ask questions before starting; implement; test; commit; self-review; report.
2. **Spec compliance review:** Verify implementation matches spec by **reading the code**. Do not trust the implementer report. Report ✅ or ❌ with specific file:line references.
3. **If spec fails:** Implementer fixes → spec reviewer re-reviews. Repeat until ✅.
4. **Code quality review:** Only after spec is ✅. Use superpowers:requesting-code-review. Report Strengths, Issues, Assessment.
5. **If quality fails:** Implementer fixes → code quality reviewer re-reviews. Repeat until approved.
6. **Mark task complete.** Proceed to next task.

**Never:** Skip reviews, start code quality before spec is ✅, move to next task with open issues, or dispatch multiple implementers in parallel.

---

## Task 1: Create Blazor Server project

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot.slnx`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\SmallEBot.csproj`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Program.cs`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\App.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\App.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\_Imports.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Routes.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Pages\_Host.cshtml`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Layout\MainLayout.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Layout\NavMenu.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Pages\Home.razor`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\wwwroot\index.html`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Pages\_ViewStart.cshtml`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Pages\_ViewImports.cshtml`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Pages\Shared\_Layout.cshtml`

**Step 1: Create solution and project**

```powershell
cd d:\RiderProjects\SmallEBot
dotnet new sln -n SmallEBot --format slnx
dotnet new blazorserver -n SmallEBot -o SmallEBot -f net10.0
dotnet sln SmallEBot.slnx add SmallEBot\SmallEBot.csproj
```

(If .NET 10 SDK is not available, use `-f net9.0` or `-f net8.0`.)

(SLNX is the XML-based solution format; use `--format sln` if you need legacy .sln.)

**Step 2: Verify build**

```powershell
dotnet build SmallEBot\SmallEBot.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```powershell
git add -A
git commit -m "chore: create Blazor Server project with .NET 10"
```

---

## Task 2: Add NuGet packages

**Files:**
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\SmallEBot.csproj`

**Step 1: Add packages**

```powershell
cd d:\RiderProjects\SmallEBot\SmallEBot
dotnet add package Microsoft.Agents.AI.OpenAI --version 1.0.0-preview.260212.1 --prerelease
dotnet add package MudBlazor --version 8.15.0
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.3
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.3
```

**Step 2: Verify restore**

```powershell
dotnet restore
```

Expected: Restore succeeded.

**Step 3: Commit**

```powershell
git add SmallEBot.csproj
git commit -m "chore: add NuGet packages (Agent Framework, MudBlazor, EF Core)"
```

---

## Task 3: Create entities and DbContext

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Data\Entities\Conversation.cs`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Data\Entities\ChatMessage.cs`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Data\AppDbContext.cs`

**Step 1: Create Conversation entity**

Create `SmallEBot\Data\Entities\Conversation.cs`:

```csharp
namespace SmallEBot.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
```

**Step 2: Create ChatMessage entity**

Create `SmallEBot\Data\Entities\ChatMessage.cs`:

```csharp
namespace SmallEBot.Data.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty; // "user" | "assistant" | "system"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
```

**Step 3: Create AppDbContext**

Create `SmallEBot\Data\AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SmallEBot.Data.Entities;

namespace SmallEBot.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserName, x.UpdatedAt });
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

**Step 4: Verify build**

```powershell
dotnet build
```

Expected: Build succeeded.

**Step 5: Commit**

```powershell
git add Data/
git commit -m "feat: add Conversation and ChatMessage entities, AppDbContext"
```

---

## Task 4: Add EF Core migration and register DbContext

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Migrations\...` (generated)
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Program.cs`

**Step 1: Add migration**

```powershell
cd d:\RiderProjects\SmallEBot\SmallEBot
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
```

Expected: Migration files created.

**Step 2: Register DbContext in Program.cs**

Add (after `var builder = WebApplication.CreateBuilder(args);`):

```csharp
builder.Services.AddDbContext<SmallEBot.Data.AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=smallebot.db"));
```

Add connection string in `appsettings.json` under `ConnectionStrings`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=smallebot.db"
}
```

**Step 3: Apply migration on startup (or run once)**

```powershell
dotnet ef database update --project SmallEBot
```

Expected: Database created.

**Step 4: Commit**

```powershell
git add Data/Migrations Program.cs appsettings.json
git commit -m "feat: add EF Core migration, register DbContext"
```

---

## Task 5: Add SmallEBot and DeepSeek config to appsettings

**Files:**
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\appsettings.json`

**Step 1: Add SmallEBot and DeepSeek sections**

Ensure `appsettings.json` contains:

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=smallebot.db"
  },
  "SmallEBot": {
    "DefaultTitle": "新对话",
    "MaxTitleLength": 20
  },
  "DeepSeek": {
    "BaseUrl": "https://api.deepseek.com",
    "Model": "deepseek-chat"
  }
}
```

**Step 2: Commit**

```powershell
git add appsettings.json
git commit -m "chore: add SmallEBot and DeepSeek config"
```

---

## Task 6: Implement ConversationService

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Services\ConversationService.cs`

**Step 1: Create ConversationService**

Create `SmallEBot\Services\ConversationService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SmallEBot.Data;
using SmallEBot.Data.Entities;

namespace SmallEBot.Services;

public class ConversationService
{
    private readonly AppDbContext _db;

    public ConversationService(AppDbContext db) => _db = db;

    public async Task<Conversation> CreateAsync(string userName, CancellationToken ct = default)
    {
        var c = new Conversation
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Title = "新对话",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Conversations.Add(c);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    public async Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default) =>
        await _db.Conversations
            .Where(x => x.UserName == userName)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await _db.Conversations
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var c = await _db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        _db.Conversations.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateTitleAsync(Guid id, string userName, string title, CancellationToken ct = default)
    {
        var c = await _db.Conversations.FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName, ct);
        if (c == null) return false;
        c.Title = title;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await _db.ChatMessages.CountAsync(x => x.ConversationId == conversationId, ct);
}
```

**Step 2: Register in Program.cs**

Add:

```csharp
builder.Services.AddScoped<SmallEBot.Services.ConversationService>();
```

**Step 3: Build**

```powershell
dotnet build
```

Expected: Build succeeded.

**Step 4: Commit**

```powershell
git add Services/ConversationService.cs Program.cs
git commit -m "feat: implement ConversationService"
```

---

## Task 7: Implement ChatMessageStoreAdapter and AgentService

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Services\ChatMessageStoreAdapter.cs`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Services\AgentService.cs`

**Step 1: Create ChatMessageStoreAdapter**

Check Microsoft.Agents.AI API for `ChatMessageStore` or `ChatHistoryProvider`. If the framework exposes `ChatMessageStore` as a feature, implement an adapter that:
- Loads `IEnumerable<ChatMessage>` from DB for the given conversationId
- Saves new messages after each run

Example structure (adjust to actual Agent Framework API):

```csharp
using Microsoft.Agents.AI; // adjust namespace from package
using Microsoft.EntityFrameworkCore;
using SmallEBot.Data;
using SmallEBot.Data.Entities;

namespace SmallEBot.Services;

/// <summary>
/// Adapts EF Core ChatMessage entities to Agent Framework's ChatMessageStore.
/// Load messages from DB, save new ones after run.
/// </summary>
public class ChatMessageStoreAdapter : ChatMessageStore // or whatever the base interface is
{
    private readonly AppDbContext _db;
    private readonly Guid _conversationId;

    public ChatMessageStoreAdapter(AppDbContext db, Guid conversationId)
    {
        _db = db;
        _conversationId = conversationId;
    }

    // Implement LoadAsync / SaveAsync per framework contract
    // See Agent Framework docs for exact interface.
}
```

If the framework uses `AgentRunOptions.Features.WithFeature<ChatMessageStore>(...)` and expects a store that loads/saves, implement accordingly. Refer to `docs/decisions/0014-feature-collections.md` and framework source.

**Step 2: Create AgentService skeleton**

Create `SmallEBot\Services\AgentService.cs`:

```csharp
using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using SmallEBot.Data;
using SmallEBot.Data.Entities;

namespace SmallEBot.Services;

public class AgentService
{
    private readonly AppDbContext _db;
    private readonly ConversationService _convSvc;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentService> _log;
    private AIAgent? _agent;

    public AgentService(
        AppDbContext db,
        ConversationService convSvc,
        IConfiguration config,
        ILogger<AgentService> log)
    {
        _db = db;
        _convSvc = convSvc;
        _config = config;
        _log = log;
    }

    private AIAgent GetAgent()
    {
        if (_agent != null) return _agent;

        var apiKey = Environment.GetEnvironmentVariable("DeepseekKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            _log.LogWarning("DeepseekKey environment variable is not set.");
        }

        var baseUrl = _config["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
        var model = _config["DeepSeek:Model"] ?? "deepseek-chat";

        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey ?? ""), options);
        var chatClient = client.GetChatClient(model);
        _agent = chatClient.AsAIAgent(
            instructions: "You are SmallEBot, a helpful personal assistant. Be concise and friendly.",
            name: "SmallEBot");
        return _agent;
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agent = GetAgent();
        var fullText = "";

        await foreach (var update in agent.RunStreamingAsync(userMessage, ct))
        {
            var text = update?.ToString() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                fullText += text;
                yield return text;
            }
        }

        // Persist user + assistant messages, generate title if first message
        var conv = await _db.Conversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserName == userName, ct);
        if (conv == null) yield break;

        _db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CreatedAt = DateTime.UtcNow
        });
        _db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = fullText,
            CreatedAt = DateTime.UtcNow
        });
        conv.UpdatedAt = DateTime.UtcNow;

        var msgCountBefore = await _convSvc.GetMessageCountAsync(conversationId, ct);
        if (msgCountBefore == 0)
        {
            var title = await GenerateTitleAsync(userMessage, ct);
            conv.Title = title;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken ct = default)
    {
        var agent = GetAgent();
        var prompt = $"Generate a very short title (under 20 chars, no quotes) for a conversation that starts with: {firstMessage}";
        try
        {
            var result = await agent.RunAsync(prompt, ct);
            var t = (result?.Text ?? "").Trim();
            if (t.Length > 20) t = t[..20];
            return string.IsNullOrEmpty(t) ? "新对话" : t;
        }
        catch
        {
            return firstMessage.Length > 20 ? firstMessage[..20] + "…" : (firstMessage or "新对话");
        }
    }
}
```

**Step 3: Add history loading**

Agent Framework may require loading past messages. If `RunStreamingAsync` accepts session/thread or options with `ChatMessageStore`, integrate `ChatMessageStoreAdapter` so the agent sees prior messages. Adjust `SendMessageStreamingAsync` to:
- Load history for `conversationId`
- Pass via `AgentRunOptions.Features.WithFeature<ChatMessageStore>(adapter)` or equivalent

Refer to framework docs at implementation time.

**Step 4: Register AgentService**

Add to Program.cs:

```csharp
builder.Services.AddScoped<SmallEBot.Services.AgentService>();
```

**Step 5: Build and fix any API mismatches**

```powershell
dotnet build
```

Fix namespaces, method signatures per Microsoft.Agents.AI.OpenAI package.

**Step 6: Commit**

```powershell
git add Services/
git commit -m "feat: implement AgentService and ChatMessageStoreAdapter skeleton"
```

---

## Task 8: Add MudBlazor and base layout

**Files:**
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Program.cs`
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Components\_Imports.razor`
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Layout\MainLayout.razor`
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\wwwroot\index.html`

**Step 1: Register MudBlazor**

In Program.cs add:

```csharp
builder.Services.AddMudServices();
```

In `Components\_Imports.razor` add:

```csharp
@using MudBlazor
```

In `wwwroot\index.html` add MudBlazor CSS/JS before `</head>` and `</body>`:

```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
...
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

**Step 2: Simplify MainLayout for chat app**

Replace MainLayout.razor with a basic MudLayout:

```razor
@inherits LayoutComponentBase
<MudThemeProvider Theme="_theme" />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudText Typo="Typo.h6">SmallEBot</MudText>
        <MudSpacer />
        @if (!string.IsNullOrEmpty(_userName))
        {
            <MudText Typo="Typo.body2">@_userName</MudText>
        }
    </MudAppBar>
    <MudMainContent>
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private MudTheme _theme = new();
    private string? _userName;

    protected override void OnInitialized()
    {
        // _userName will be set from parent/page
    }

    public void SetUserName(string? name) => _userName = name;
}
```

Note: UserName will be passed from ChatPage. Consider a shared state service or cascading value.

**Step 3: Build**

```powershell
dotnet build
```

**Step 4: Commit**

```powershell
git add Program.cs Components/_Imports.razor Components/Layout/MainLayout.razor wwwroot/index.html
git commit -m "feat: add MudBlazor, base layout"
```

---

## Task 9: Create UserNameService and UserNameDialog

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Services\UserNameService.cs`
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Chat\UserNameDialog.razor`

**Step 1: Create UserNameService**

Create `Services\UserNameService.cs`:

```csharp
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SmallEBot.Services;

public class UserNameService
{
    private const string Key = "smallebot-username";
    private readonly ProtectedSessionStorage _storage;

    public UserNameService(ProtectedSessionStorage storage) => _storage = storage;

    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _storage.GetAsync<string>(Key, ct);
            return r.Success ? r.Value : null;
        }
        catch { return null; }
    }

    public async Task SetAsync(string userName, CancellationToken ct = default) =>
        await _storage.SetAsync(Key, userName, ct);
}
```

Register: `builder.Services.AddScoped<UserNameService>();`

**Step 2: Create UserNameDialog**

Create `Components\Chat\UserNameDialog.razor`:

```razor
@using MudBlazor

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Welcome to SmallEBot</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_userName"
                     Label="Enter your name"
                     Required
                     RequiredError="Name is required"
                     Immediate="true" />
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="Submit">Start</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] MudDialogInstance MudDialog { get; set; } = null!;
    private string _userName = "";

    private void Submit() => MudDialog.Close(DialogResult.Ok(_userName.Trim()));
}
```

**Step 3: Commit**

```powershell
git add Services/UserNameService.cs Components/Chat/UserNameDialog.razor Program.cs
git commit -m "feat: add UserNameService and UserNameDialog"
```

---

## Task 10: Create ConversationSidebar component

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Chat\ConversationSidebar.razor`

**Step 1: Create ConversationSidebar**

Create `Components\Chat\ConversationSidebar.razor`:

```razor
@using SmallEBot.Data.Entities
@using SmallEBot.Services
@inject ConversationService ConvSvc
@inject ISnackbar Snackbar

<MudDrawer Open="@_open" Variant="Variant.Persistent" Anchor="Anchor.Left">
    <MudDrawerHeader>
        <MudText Typo="Typo.h6">Conversations</MudText>
    </MudDrawerHeader>
    <MudNavMenu>
        <MudMenuItem Icon="@Icons.Material.Filled.Add" OnClick="CreateNew">New</MudMenuItem>
        @foreach (var c in _conversations)
        {
            <MudListItem Key="@c.Id"
                         Text="@(string.IsNullOrEmpty(c.Title) ? "新对话" : c.Title)"
                         SecondaryText="@c.UpdatedAt.ToString("g")"
                         Selected="@(SelectedId == c.Id)"
                         OnClick="@(() => SelectConversation(c.Id))">
                <MudIconButton Icon="@Icons.Material.Filled.Delete"
                               Size="Size.Small"
                               OnClick="@(() => DeleteConversation(c.Id))"
                               OnClick:stopPropagation="true" />
            </MudListItem>
        }
    </MudNavMenu>
</MudDrawer>

@code {
    [Parameter] public Guid? SelectedId { get; set; }
    [Parameter] public EventCallback<Guid> OnSelect { get; set; }
    [Parameter] public EventCallback<Guid> OnDelete { get; set; }
    [Parameter] public EventCallback OnCreate { get; set; }
    [Parameter] public string UserName { get; set; } = "";
    [Parameter] public bool Open { get; set; } = true;

    private bool _open = true;
    private List<Conversation> _conversations = new();

    protected override async Task OnParametersSetAsync()
    {
        _open = Open;
        if (!string.IsNullOrEmpty(UserName))
            await LoadAsync();
    }

    public async Task LoadAsync()
    {
        _conversations = await ConvSvc.GetListAsync(UserName);
        StateHasChanged();
    }

    private async Task CreateNew() => await OnCreate.InvokeAsync();
    private async Task SelectConversation(Guid id) => await OnSelect.InvokeAsync(id);
    private async Task DeleteConversation(Guid id)
    {
        var ok = await ConvSvc.DeleteAsync(id, UserName);
        if (ok)
        {
            await OnDelete.InvokeAsync(id);
            await LoadAsync();
        }
        else
            Snackbar.Add("Delete failed", Severity.Error);
    }
}
```

**Step 2: Fix MudListItem/delete button layout**

MudListItem may not support child IconButton directly. Use MudListItem with `Avatar` or place delete in `SecondaryContent`. Adjust per MudBlazor API.

**Step 3: Commit**

```powershell
git add Components/Chat/ConversationSidebar.razor
git commit -m "feat: add ConversationSidebar component"
```

---

## Task 11: Create ChatArea component

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Chat\ChatArea.razor`

**Step 1: Create ChatArea**

Create `Components\Chat\ChatArea.razor`:

```razor
@using SmallEBot.Data.Entities
@using SmallEBot.Services
@inject AgentService AgentSvc
@inject ISnackbar Snackbar

<MudPaper Class="pa-4" Elevation="2">
    @if (Messages.Any())
    {
        @foreach (var m in Messages)
        {
            <MudChat ChatPosition="@(m.Role == "user" ? ChatBubblePosition.End : ChatBubblePosition.Start)">
                <MudAvatar>@(m.Role == "user" ? "U" : "B")</MudAvatar>
                <MudChatHeader Name="@(m.Role == "user" ? "You" : "SmallEBot")" Time="@m.CreatedAt" />
                <MudChatBubble>@((MarkupString)System.Net.WebUtility.HtmlEncode(m.Content))</MudChatBubble>
            </MudChat>
        }
    }
    @if (_streaming)
    {
        <MudChat ChatPosition="ChatBubblePosition.Start">
            <MudAvatar>B</MudAvatar>
            <MudChatHeader Name="SmallEBot" Time="@DateTime.Now" />
            <MudChatBubble>@_streamingText</MudChatBubble>
        </MudChat>
    }

    <MudStack Row Spacing="2" Class="mt-4">
        <MudTextField @bind-Value="_input"
                      Label="Message"
                      Variant="Variant.Outlined"
                      FullWidth
                      Immediate="true"
                      OnKeyDown="OnKeyDown" />
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   OnClick="Send"
                   Disabled="@(_streaming || string.IsNullOrWhiteSpace(_input))">
            Send
        </MudButton>
    </MudStack>
</MudPaper>

@code {
    [Parameter] public List<ChatMessage> Messages { get; set; } = new();
    [Parameter] public Guid? ConversationId { get; set; }
    [Parameter] public string UserName { get; set; } = "";
    [Parameter] public EventCallback OnMessageSent { get; set; }

    private string _input = "";
    private bool _streaming;
    private string _streamingText = "";

    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(_input) || !ConversationId.HasValue) return;
        var msg = _input.Trim();
        _input = "";
        _streaming = true;
        _streamingText = "";
        StateHasChanged();

        try
        {
            await foreach (var chunk in AgentSvc.SendMessageStreamingAsync(ConversationId!.Value, UserName, msg))
            {
                _streamingText += chunk;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            _streaming = false;
            await OnMessageSent.InvokeAsync();
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey) await Send();
    }
}
```

**Step 2: Fix MudChatBubble content**

Use `@m.Content` directly; Markdown/MudMarkdown if needed. Remove HtmlEncode if content is safe.

**Step 3: Commit**

```powershell
git add Components/Chat/ChatArea.razor
git commit -m "feat: add ChatArea component with streaming"
```

---

## Task 12: Create ChatPage and wire routing

**Files:**
- Create: `d:\RiderProjects\SmallEBot\SmallEBot\Components\Pages\ChatPage.razor`
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Components\App.razor` or Routes

**Step 1: Create ChatPage**

Create `Components\Pages\ChatPage.razor`:

```razor
@page "/"
@layout MainLayout
@using SmallEBot.Data.Entities
@using SmallEBot.Services
@inject UserNameService UserNameSvc
@inject ConversationService ConvSvc
@inject IDialogService DialogSvc

@if (string.IsNullOrEmpty(_userName))
{
    <p>Loading...</p>
}
else
{
    <MudLayout>
        <ConversationSidebar UserName="_userName"
                             SelectedId="_selectedId"
                             OnSelect="OnSelectConversation"
                             OnCreate="OnCreateConversation"
                             OnDelete="OnDeleteConversation"
                             Open="true" />
        <MudMainContent>
            @if (_selectedConversation != null)
            {
                <ChatArea Messages="_selectedConversation.Messages.ToList()"
                          ConversationId="_selectedConversation.Id"
                          UserName="_userName"
                          OnMessageSent="RefreshSidebar" />
            }
            else
            {
                <MudText>Select or create a conversation.</MudText>
            }
        </MudMainContent>
    </MudLayout>
}

@code {
    private string? _userName;
    private Guid? _selectedId;
    private Conversation? _selectedConversation;

    protected override async Task OnInitializedAsync()
    {
        _userName = await UserNameSvc.GetAsync();
        if (string.IsNullOrEmpty(_userName))
        {
            var result = await DialogSvc.ShowAsync<UserNameDialog>("Welcome");
            if (result.Canceled || string.IsNullOrWhiteSpace(result.Data as string))
                return;
            _userName = (result.Data as string)!.Trim();
            await UserNameSvc.SetAsync(_userName);
        }
        await LoadConversations();
    }

    private async Task LoadConversations()
    {
        if (string.IsNullOrEmpty(_userName)) return;
        var list = await ConvSvc.GetListAsync(_userName);
        if (list.Count > 0 && !_selectedId.HasValue)
            await OnSelectConversation(list[0].Id);
    }

    private async Task OnSelectConversation(Guid id)
    {
        _selectedId = id;
        _selectedConversation = await ConvSvc.GetByIdAsync(id, _userName!);
        StateHasChanged();
    }

    private async Task OnCreateConversation()
    {
        var c = await ConvSvc.CreateAsync(_userName!);
        _selectedId = c.Id;
        _selectedConversation = c;
        StateHasChanged();
    }

    private async Task OnDeleteConversation(Guid id)
    {
        if (_selectedId == id)
        {
            _selectedId = null;
            _selectedConversation = null;
        }
        await LoadConversations();
    }

    private async Task RefreshSidebar()
    {
        if (_selectedId.HasValue)
            _selectedConversation = await ConvSvc.GetByIdAsync(_selectedId.Value, _userName!);
        StateHasChanged();
    }
}
```

**Step 2: Update routing**

Ensure `Routes.razor` or `App.razor` has `@page "/"` for ChatPage or redirect Home to ChatPage. Replace `Home.razor` as default if needed.

**Step 3: Build**

```powershell
dotnet build
```

**Step 4: Commit**

```powershell
git add Components/Pages/ChatPage.razor
git commit -m "feat: add ChatPage, wire conversation and chat flow"
```

---

## Task 13: Wire history into Agent (ChatMessageStore)

**Files:**
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Services\AgentService.cs`
- Modify: `d:\RiderProjects\SmallEBot\SmallEBot\Services\ChatMessageStoreAdapter.cs`

**Step 1: Implement history loading**

Per Microsoft Agent Framework docs, pass prior messages to the agent. If the framework uses `AgentRunOptions.Features.WithFeature<ChatMessageStore>(...)`:
- Load `ChatMessage` records for the conversation from DB
- Convert to framework `ChatMessage` type
- Inject via the store/feature

If it uses `AgentSession` or `thread` with messages, load and attach history before `RunStreamingAsync`. Implement per actual API.

**Step 2: Verify streaming still works**

Run app, send multiple messages in one conversation, confirm agent sees prior context.

**Step 3: Commit**

```powershell
git add Services/
git commit -m "feat: wire conversation history into Agent"
```

---

## Task 14: Manual verification and polish

**Files:**
- Various

**Step 1: Run app**

```powershell
dotnet run
```

Set `DeepseekKey` in environment or launchSettings.json for development.

**Step 2: Verify**

- First visit shows UserName dialog
- Create conversation, send message, see streaming
- Send second message, agent has context
- Create new conversation, delete old one
- Switch between conversations

**Step 3: Fix any UI/layout issues**

Adjust MudDrawer, MudListItem, responsive behavior per design.

**Step 4: Commit**

```powershell
git add -A
git commit -m "chore: polish UI and verify Phase 1 flow"
```

---

## Execution Options

**Plan saved to** `docs/plans/2025-02-13-smallebot-phase1-implementation.md`

**Two execution options:**

1. **Subagent-Driven (this session)** – Execute tasks one by one in this session, review between tasks.
2. **Parallel Session** – Open a new session with executing-plans for batch execution with checkpoints.

**Which approach?**
