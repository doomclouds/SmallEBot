# SmallEBot Phase 1 Design Document

**Status:** Draft — Pending review  
**Date:** 2026-02-14  
**Target:** .NET 10, Blazor Server, Microsoft Agent Framework, MudBlazor

---

## 1. Overview

SmallEBot is a personal chat assistant built with ASP.NET Core Blazor Server. Phase 1 focuses on:
- Multi-conversation support
- Conversation history (list, switch, delete)
- Streaming chat responses
- Agent-generated conversation titles

Future phases will add ToolCall, MCP, and Skills support. The design keeps the Agent as the central abstraction to simplify later extensions.

---

## 2. Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Blazor UI (MudBlazor)                                       │
│  - First visit: UserName input dialog                        │
│  - Sidebar: Conversation list (new, switch, delete)          │
│  - Main: MudChat + input, streaming output                  │
└─────────────────────────────────────────────────────────────┘
                              │ SignalR
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core Host                                           │
│  - UserName in ProtectedSessionStorage                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  Microsoft Agent Framework + DeepSeek (OpenAI-compatible)     │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  SQLite (EF Core) — conversations isolated by UserName        │
└─────────────────────────────────────────────────────────────┘
```

**Tech Stack**

| Category   | Choice                                              |
|-----------|------------------------------------------------------|
| Runtime   | .NET 10                                              |
| UI        | Blazor Server + MudBlazor                             |
| Agent     | Microsoft.Agents.AI.OpenAI (preview)                  |
| LLM       | DeepSeek Chat (OpenAI-compatible API)                |
| ORM       | Entity Framework Core                                |
| Database  | SQLite                                               |

**User Identity**

- No login/auth
- First visit: prompt for username via dialog
- Storage: `ProtectedSessionStorage`
- Data: conversations filtered by `UserName`

---

## 3. NuGet Packages (Latest)

| Package                       | Version                        | Notes                              |
|-------------------------------|--------------------------------|------------------------------------|
| Microsoft.Agents.AI.OpenAI    | 1.0.0-preview.260212.1         | Agent Framework for OpenAI-compatible APIs (brings Microsoft.Agents.AI transitively) |
| MudBlazor                     | 8.15.0                         | UI components                      |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.3 | SQLite provider for EF Core       |
| Microsoft.EntityFrameworkCore.Design | 10.0.3 | Migrations                        |

> Use `IncludePrerelease` when resolving Microsoft.Agents.AI.OpenAI. The package includes OpenAI client and Agent extensions; no separate `Microsoft.Agents.AI` or `OpenAI` reference needed.

---

## 4. Configuration

**appsettings.json**

```json
{
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

**API Key**

- Environment variable: `DeepseekKey`
- Example: `Environment.GetEnvironmentVariable("DeepseekKey")`
- No key in config files or source control

**Client Setup (DeepSeek via Microsoft.Agents.AI.OpenAI)**

```csharp
// Microsoft.Agents.AI.OpenAI provides OpenAIClient, ChatClient, AsAIAgent
var apiKey = Environment.GetEnvironmentVariable("DeepseekKey");
var baseUrl = "https://api.deepseek.com"; // Add /v1 if SDK requires it

var clientOptions = new OpenAIClientOptions() { Endpoint = new Uri(baseUrl) };
var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
var chatClient = client.GetChatClient("deepseek-chat");
var agent = chatClient.AsAIAgent(instructions: "...", name: "SmallEBot");
```

---

## 5. Data Model

**Conversation**

| Column      | Type      | Description                             |
|-------------|-----------|-----------------------------------------|
| Id          | Guid      | PK                                      |
| UserName    | string    | User isolation                          |
| Title       | string    | Agent-generated from first message      |
| CreatedAt   | DateTime  | Created                                 |
| UpdatedAt   | DateTime  | Last activity                           |

**ChatMessage**

| Column          | Type    | Description                    |
|-----------------|---------|--------------------------------|
| Id              | Guid    | PK                             |
| ConversationId  | Guid    | FK → Conversation              |
| Role            | string  | "user" | "assistant" | "system" |
| Content         | string  | Message text                   |
| CreatedAt       | DateTime| Time sent                      |

**Relations**

- 1 Conversation : N ChatMessages
- Indexes: `Conversation(UserName, UpdatedAt)`, `ChatMessage(ConversationId, CreatedAt)`

**Title Generation**

- After first user message and agent reply, call Agent once more with a short prompt to generate a title.
- Fallback: truncate first message or use `DefaultTitle` on failure.

---

## 6. Services

### ConversationService

| Method                      | Description                                                      |
|-----------------------------|------------------------------------------------------------------|
| `CreateAsync(userName)`     | Create conversation, return entity                              |
| `GetListAsync(userName)`    | List conversations, ordered by `UpdatedAt` desc                  |
| `GetByIdAsync(id, userName)`| Get by id with user check                                        |
| `DeleteAsync(id, userName)` | Delete conversation and messages                                |
| `UpdateTitleAsync(...)`     | Update title                                                    |

### AgentService

| Method                              | Description                                                  |
|-------------------------------------|--------------------------------------------------------------|
| `SendMessageStreamingAsync(...)`    | Send message, stream agent reply as chunks                  |
| `GenerateTitleAsync(firstMessage)`  | One-off title generation (non-streaming)                    |

**SendMessageStreamingAsync flow**

1. Save user message to DB
2. Load history for `conversationId` into `ChatMessageStore`
3. Call `agent.RunStreamingAsync(...)`
4. Return `IAsyncEnumerable<string>`
5. On completion, persist full assistant message
6. If first message, call `GenerateTitleAsync` and update `Conversation.Title`

**ChatMessageStore**

- Implement Agent Framework’s `ChatMessageStore` / `ChatHistoryProvider`
- Read/write `ChatMessage` via EF Core for the given `conversationId`

---

## 7. UI Structure

**Layout**

```
┌──────────────────────────────────────────────────────────────────┐
│  AppBar (SmallEBot + username)                                    │
├─────────────┬────────────────────────────────────────────────────┤
│ Sidebar     │  Chat area                                          │
│ - New       │  - Message list (MudChat / MudChatBubble)            │
│ - List      │  - Input + Send                                     │
│ - Delete    │  - Streaming: append chunks to current assistant msg│
└─────────────┴────────────────────────────────────────────────────┘
```

**Components**

- `MudLayout`, `MudDrawer` for layout
- `MudList` for conversations
- `MudChat` / `MudChatBubble` for messages
- `MudTextField` for input
- `MudDialog` for username prompt on first visit
- `MudIconButton` for delete

**Streaming behavior**

- Append chunks to `currentAssistantText`
- Call `StateHasChanged()` periodically
- Persist full text when stream completes

---

## 8. Project Structure

```
SmallEBot/
├── Data/
│   ├── AppDbContext.cs
│   ├── Entities/
│   │   ├── Conversation.cs
│   │   └── ChatMessage.cs
│   └── Migrations/
├── Services/
│   ├── ConversationService.cs
│   ├── AgentService.cs
│   └── ChatMessageStoreAdapter.cs
├── Components/
│   ├── Layout/
│   ├── Chat/
│   │   ├── ChatPage.razor
│   │   ├── ConversationSidebar.razor
│   │   ├── ChatArea.razor
│   │   └── UserNameDialog.razor
│   └── Shared/
├── Pages/
├── wwwroot/
└── Program.cs
```

---

## 9. Error Handling

| Scenario               | Action                                                        |
|------------------------|---------------------------------------------------------------|
| Missing DeepseekKey    | Log warning at startup; on first call, show Snackbar          |
| LLM timeout/network    | try-catch, Snackbar, do not save failed assistant message     |
| Stream interruption    | Save partial content or mark failed; allow retry              |
| DB error               | Log, Snackbar "Operation failed, please retry"                |
| Empty username         | Require input before entering main UI                        |
| Title generation fail  | Use truncated first message or `DefaultTitle`                |

---

## 10. Extensibility (Future Phases)

**ToolCall / MCP / Skills**

- Extend `AgentService` in `Services/`
- Register tools/MCP/Skills on the same `AIAgent` used for chat
- Keep `ChatMessageStore` and conversation model unchanged

**Suggested entry points**

- `AgentService` constructor / factory: create agent and register extensions
- `Services/Agent/` or `Services/Tools/` for new capabilities
- Configuration for enabled tools/MCP/Skills

---

## 11. Summary Checklist

- [x] .NET 10
- [x] Blazor Server + MudBlazor
- [x] Microsoft.Agents.AI.OpenAI (latest preview)
- [x] DeepSeek Chat, API key from `DeepseekKey`
- [x] SQLite + EF Core
- [x] No login; first-visit username dialog
- [x] Multi-conversation (create, switch, delete)
- [x] Streaming responses
- [x] Agent-generated titles
- [x] Design extensible for ToolCall, MCP, Skills

---

*Document approved: Pending user sign-off. After approval, implementation plan will be created with superpowers:writing-plans.*
