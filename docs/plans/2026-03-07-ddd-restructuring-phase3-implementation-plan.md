# SmallEBot DDD Restructuring - Phase 3: Application Layer

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Unify service interfaces, implement domain services, and refactor application services to depend on Domain abstractions, reducing coupling between Blazor UI and Host implementations.

**Architecture:** Three-layer approach: (1) Domain layer defines service interfaces (ICompressionService, IContextWindowEstimator), (2) Application layer implements orchestration services (AgentConversationService) using Domain interfaces, (3) Host layer provides concrete implementations (CompressionService, AgentCacheService) implementing Domain interfaces.

**Tech Stack:** .NET 10, Microsoft.Extensions.AI, Microsoft.Extensions.DependencyInjection

---

## Prerequisites

Phase 1 (Domain Layer) and Phase 2 (Infrastructure Layer) must be complete:
- `SmallEBot.Domain` project with all domain types and service interfaces
- `SmallEBot.Infrastructure` project with repository implementations
- No compilation errors

---

## Current State Analysis

### Interface Duplication

| Interface | Domain Layer | Application Layer | Issue |
|-----------|-------------|-------------------|-------|
| `ICompressionService` | ✅ `Domain/Conversations/Services/` | ✅ `Application/Conversation/` | Duplicate, same signature |
| `IContextWindowEstimator` | ✅ `Domain/Conversations/Services/` | ⚠️ `IContextUsageEstimator` | Similar, different name |
| `ContextUsageEstimate` | ✅ Record in Domain | ✅ Class in Core | Duplicate |

### Implementation Locations

| Service | Current Location | Interface | Migration |
|---------|-----------------|-----------|-----------|
| `CompressionService` | Host/Services/Agent/ | Application's ICompressionService | Keep in Host, use Domain interface |
| `AgentCacheService` | Host/Services/Agent/ | Application's IContextUsageEstimator | Implement Domain's IContextWindowEstimator |
| `ContextWindowManager` | Host/Services/Context/ | Application's IContextWindowManager | Move to Application layer |

### Core Layer Models to Migrate

| Model | Current Location | Target Location |
|-------|-----------------|-----------------|
| `Conversation` | Core/Entities/ | DELETE - use Domain's ConversationMetadata |
| `ConversationMetadata` | Core/Models/ | DELETE - duplicate of Domain entity |
| `TurnMetadata` | Core/Models/ | DELETE - duplicate of Domain's TurnInfo |
| `ContextUsageEstimate` | Core/Models/ | DELETE - use Domain's record |
| `StreamUpdate` | Core/Models/ | Keep in Application or move to Domain |
| `AssistantSegment` | Core/Models/ | Keep in Application |

---

## Task 3.1: Unify ICompressionService Interface

**Files:**
- Modify: `SmallEBot.Application/Conversation/ICompressionService.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`
- Modify: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 1: Remove duplicate interface from Application layer**

The Application layer's `ICompressionService` should simply re-export the Domain interface:

```csharp
// SmallEBot.Application/Conversation/ICompressionService.cs
// Re-export Domain's ICompressionService for backward compatibility
// Deprecated: Use SmallEBot.Domain.Conversations.Services.ICompressionService directly

namespace SmallEBot.Application.Conversation;

/// <summary>
/// Re-export of Domain's ICompressionService for backward compatibility.
/// Deprecated: Use SmallEBot.Domain.Conversations.Services.ICompressionService directly.
/// </summary>
[Obsolete("Use SmallEBot.Domain.Conversations.Services.ICompressionService directly.")]
public interface ICompressionService : Domain.Conversations.Services.ICompressionService
{
}
```

**Step 2: Update AgentConversationService to use Domain interface**

```csharp
// SmallEBot.Application/Conversation/AgentConversationService.cs
// Line 13: Change from Application's ICompressionService to Domain's
using ICompressionService = SmallEBot.Domain.Conversations.Services.ICompressionService;
```

**Step 3: Update CompressionService to use Domain interface**

```csharp
// SmallEBot/Services/Agent/CompressionService.cs
// Line 10: Change to implement Domain's interface
using SmallEBot.Domain.Conversations.Services;

namespace SmallEBot.Services.Agent;

public sealed class CompressionService(IAgentBuilder agentBuilder, ILogger<CompressionService> logger)
    : ICompressionService
{
    // ... existing implementation unchanged
}
```

**Step 4: Update DI registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
// Change registration to use Domain interface
services.AddScoped<Domain.Conversations.Services.ICompressionService, CompressionService>();
```

**Step 5: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Application/Conversation/ICompressionService.cs SmallEBot.Application/Conversation/AgentConversationService.cs SmallEBot/Services/Agent/CompressionService.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: unify ICompressionService to Domain layer"
```

---

## Task 3.2: Unify IContextWindowEstimator Interface

**Files:**
- Create: `SmallEBot.Application/Conversation/IContextUsageEstimator.cs` (update)
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`
- Modify: `SmallEBot/Services/Agent/AgentCacheService.cs`

**Step 1: Update IContextUsageEstimator to extend Domain interface**

```csharp
// SmallEBot.Application/Conversation/IContextUsageEstimator.cs
using SmallEBot.Domain.Conversations.Services;

namespace SmallEBot.Application.Conversation;

/// <summary>
/// Extends Domain's IContextWindowEstimator with UI-specific estimation.
/// </summary>
public interface IContextUsageEstimator : IContextWindowEstimator
{
    /// <summary>
    /// Get detailed context usage estimate for UI display.
    /// Returns null if estimation is not available.
    /// </summary>
    new Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
```

**Step 2: Update AgentCacheService to implement both interfaces**

```csharp
// SmallEBot/Services/Agent/AgentCacheService.cs
using SmallEBot.Domain.Conversations.Services;

namespace SmallEBot.Services.Agent;

public class AgentCacheService(/* dependencies */) : IAsyncDisposable, IContextUsageEstimator
{
    // Implement IContextWindowEstimator (Domain)
    public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        // ... existing implementation
    }

    // Explicit interface implementation for Domain's simpler interface if needed
    Task<ContextUsageEstimate?> IContextWindowEstimator.GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct)
        => GetEstimatedContextUsageDetailAsync(conversationId, ct);
}
```

**Step 3: Update DI registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
services.AddScoped<IContextUsageEstimator, AgentCacheService>();
services.AddScoped<IContextWindowEstimator>(sp => sp.GetRequiredService<IContextUsageEstimator>());
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Application/Conversation/IContextUsageEstimator.cs SmallEBot/Services/Agent/AgentCacheService.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: unify IContextWindowEstimator with Application's IContextUsageEstimator"
```

---

## Task 3.3: Create ITokenizer Interface in Domain Layer

**Files:**
- Create: `SmallEBot.Domain/Common/Services/ITokenizer.cs`
- Modify: `SmallEBot/Services/Agent/Tokenizer.cs`

**Step 1: Create ITokenizer interface**

```csharp
// SmallEBot.Domain/Common/Services/ITokenizer.cs
namespace SmallEBot.Domain.Common.Services;

/// <summary>
/// Tokenizer for counting tokens in text.
/// Used for context window estimation and compression.
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// Counts the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to count tokens for.</param>
    /// <returns>The estimated number of tokens.</returns>
    int CountTokens(string text);
}
```

**Step 2: Update Tokenizer to implement ITokenizer**

```csharp
// SmallEBot/Services/Agent/Tokenizer.cs
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Services.Agent;

/// <summary>
/// Tokenizer implementation using various strategies.
/// </summary>
public interface ITokenizer : Domain.Common.Services.ITokenizer
{
    // Keep existing interface for backward compatibility
}

// Update implementation class
public sealed class DeepSeekTokenizer : Domain.Common.Services.ITokenizer, IDisposable
{
    // ... existing implementation
    public int CountTokens(string text) => /* existing logic */;
}

public sealed class CharEstimateTokenizer : Domain.Common.Services.ITokenizer
{
    public int CountTokens(string text) => text.Length / 4;
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Domain/Common/Services/ITokenizer.cs SmallEBot/Services/Agent/Tokenizer.cs
git commit -m "feat(domain): add ITokenizer interface for token counting abstraction"
```

---

## Task 3.4: Move ContextWindowManager to Application Layer

**Files:**
- Create: `SmallEBot.Application/Context/ContextWindowManager.cs`
- Modify: `SmallEBot/Services/Context/ContextWindowManager.cs` (make it a thin wrapper or delete)

**Step 1: Create ContextWindowManager in Application layer**

```csharp
// SmallEBot.Application/Context/ContextWindowManager.cs
using Microsoft.Extensions.AI;
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Application.Context;

/// <summary>
/// Manages context window using tokenizer for estimation.
/// Note: EstimateTokens and TrimToFit count only message Content (and a small role overhead);
/// they do not include tool calls (name, arguments, result) or think blocks.
/// </summary>
public sealed class ContextWindowManager(ITokenizer tokenizer) : IContextWindowManager
{
    /// <summary>
    /// Estimates tokens for the given messages.
    /// Counts only each message's Content plus role overhead; tool calls and think blocks are not included.
    /// </summary>
    public int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return 0;
        var total = 0;
        foreach (var msg in messages)
        {
            var text = msg.Text ?? "";
            total += tokenizer.CountTokens(text);
            total += 4; // role overhead estimate
        }
        return total;
    }

    /// <summary>
    /// Trims messages to fit within maxTokens.
    /// Only message Content is considered; tool/think tokens are not part of this budget.
    /// </summary>
    public TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        if (messages.Count == 0)
            return new TrimResult([], 0, 0);

        var tokens = EstimateTokens(messages);
        if (tokens <= maxTokens)
            return new TrimResult(messages, tokens, 0);

        // Keep newest messages, trim oldest
        var result = new List<ChatMessage>();
        var currentTokens = 0;
        var trimmed = 0;

        // Iterate from newest to oldest
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var text = msg.Text ?? "";
            var msgTokens = tokenizer.CountTokens(text) + 4;
            if (currentTokens + msgTokens <= maxTokens)
            {
                result.Insert(0, msg);
                currentTokens += msgTokens;
            }
            else
            {
                trimmed++;
            }
        }

        return new TrimResult(result, currentTokens, trimmed);
    }
}

/// <summary>
/// Result of trimming messages to fit context window.
/// </summary>
/// <param name="Messages">The trimmed messages.</param>
/// <param name="TokenCount">The token count of the trimmed messages.</param>
/// <param name="TrimmedCount">Number of messages that were trimmed.</param>
public record TrimResult(IReadOnlyList<ChatMessage> Messages, int TokenCount, int TrimmedCount);
```

**Step 2: Update IContextWindowManager interface**

```csharp
// SmallEBot.Application/Context/IContextWindowManager.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Context;

/// <summary>
/// Manages context window for conversations.
/// </summary>
public interface IContextWindowManager
{
    /// <summary>
    /// Estimates the token count for the given messages.
    /// </summary>
    int EstimateTokens(IReadOnlyList<ChatMessage> messages);

    /// <summary>
    /// Trims messages to fit within the specified token limit.
    /// </summary>
    TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens);
}
```

**Step 3: Update DI registration in Host layer**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
services.AddScoped<IContextWindowManager, ContextWindowManager>();
```

**Step 4: Delete or deprecate Host layer's ContextWindowManager**

Delete: `SmallEBot/Services/Context/ContextWindowManager.cs`

**Step 5: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Application/Context/ SmallEBot/Extensions/ServiceCollectionExtensions.cs
git rm SmallEBot/Services/Context/ContextWindowManager.cs
git commit -m "refactor: move ContextWindowManager to Application layer"
```

---

## Task 3.5: Create Application Service Interfaces for UI Abstraction

**Files:**
- Create: `SmallEBot.Application/Agents/IAgentConfigService.cs`
- Create: `SmallEBot.Application/Agents/ISkillsConfigService.cs`
- Create: `SmallEBot.Application/Agents/IModelConfigService.cs`
- Create: `SmallEBot.Application/Workspace/IWorkspaceUploadService.cs`

**Step 1: Create IAgentConfigService in Application layer**

```csharp
// SmallEBot.Application/Agents/IAgentConfigService.cs
using SmallEBot.Domain.Agents;

namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing agent configurations.
/// Abstracts Host layer implementation from Blazor UI.
/// </summary>
public interface IAgentConfigService
{
    /// <summary>
    /// Gets the default agent configuration.
    /// </summary>
    Task<AgentConfig?> GetDefaultAgentConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets an agent configuration by ID.
    /// </summary>
    Task<AgentConfig?> GetAgentConfigAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Gets all agent configurations.
    /// </summary>
    Task<IReadOnlyList<AgentConfig>> GetAllAgentConfigsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the tool result max length for compression.
    /// </summary>
    int GetToolResultMaxLength();

    /// <summary>
    /// Gets the compression threshold ratio.
    /// </summary>
    double GetCompressionThreshold();
}
```

**Step 2: Create ISkillsConfigService in Application layer**

```csharp
// SmallEBot.Application/Agents/ISkillsConfigService.cs
namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing skills configuration.
/// Abstracts Host layer implementation from Blazor UI.
/// </summary>
public interface ISkillsConfigService
{
    /// <summary>
    /// Gets the list of available skill IDs.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableSkillIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a skill is available.
    /// </summary>
    Task<bool> IsSkillAvailableAsync(string skillId, CancellationToken ct = default);
}
```

**Step 3: Create IWorkspaceUploadService in Application layer**

```csharp
// SmallEBot.Application/Workspace/IWorkspaceUploadService.cs
namespace SmallEBot.Application.Workspace;

/// <summary>
/// Application service for workspace file uploads.
/// Abstracts Host layer implementation from Blazor UI.
/// </summary>
public interface IWorkspaceUploadService
{
    /// <summary>
    /// Uploads files to the workspace.
    /// </summary>
    Task<IReadOnlyList<string>> UploadFilesAsync(
        IReadOnlyList<Stream> files,
        IReadOnlyList<string> fileNames,
        CancellationToken ct = default);
}
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Application/Agents/ SmallEBot.Application/Workspace/
git commit -m "feat(app): add application service interfaces for UI abstraction"
```

---

## Task 3.6: Refactor Host Services to Implement Application Interfaces

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentConfigService.cs`
- Modify: `SmallEBot/Services/Skills/SkillsConfigService.cs`
- Modify: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Update AgentConfigService to implement IAgentConfigService**

```csharp
// SmallEBot/Services/Agent/AgentConfigService.cs
using SmallEBot.Application.Agents;
using SmallEBot.Domain.Agents;

namespace SmallEBot.Services.Agent;

/// <summary>
/// Host implementation of IAgentConfigService.
/// </summary>
public class AgentConfigService : IAgentConfigService
{
    private readonly IAgentConfigRepository _repository;
    private readonly IUserPreferenceRepository _userPrefs;

    public AgentConfigService(
        IAgentConfigRepository repository,
        IUserPreferenceRepository userPrefs)
    {
        _repository = repository;
        _userPrefs = userPrefs;
    }

    public async Task<AgentConfig?> GetDefaultAgentConfigAsync(CancellationToken ct = default)
        => await _repository.GetDefaultAsync(ct);

    public async Task<AgentConfig?> GetAgentConfigAsync(string id, CancellationToken ct = default)
        => await _repository.GetByIdAsync(id, ct);

    public async Task<IReadOnlyList<AgentConfig>> GetAllAgentConfigsAsync(CancellationToken ct = default)
        => await _repository.GetAllAsync(ct);

    public int GetToolResultMaxLength()
    {
        // Implementation using user preferences
        return 2000; // Default or from config
    }

    public double GetCompressionThreshold()
    {
        // Implementation using user preferences
        return 0.8; // Default 80%
    }
}

// Keep old interface for backward compatibility
public interface IAgentConfigServiceLegacy
{
    // Old interface members...
}
```

**Step 2: Update DI registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
// Register Application layer interfaces with Host implementations
services.AddScoped<IAgentConfigService, AgentConfigService>();
services.AddScoped<ISkillsConfigService, SkillsConfigService>();
services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Services/ SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(host): implement Application service interfaces"
```

---

## Task 3.7: Create Application DI Registration

**Files:**
- Create: `SmallEBot.Application/ServiceCollectionExtensions.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Create Application layer DI registration**

```csharp
// SmallEBot.Application/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Application.Agents;
using SmallEBot.Application.Context;
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Session;
using SmallEBot.Application.Streaming;
using SmallEBot.Application.Workspace;

namespace SmallEBot.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Context management
        services.AddScoped<IContextWindowManager, ContextWindowManager>();

        // Note: ICompressionService and IContextUsageEstimator are implemented in Host layer
        // because they depend on AI services (IAgentBuilder, ITokenizer)

        // Session services - interfaces only, implementations in Infrastructure
        // ISessionFileService, ISessionManager, IAgentSessionReader are in Infrastructure

        // Streaming - interfaces only, implementations in Host
        // IAgentRunner, IStreamSink are in Host

        return services;
    }
}
```

**Step 2: Update Host layer to call AddApplication**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddSmallEBotServices(
    this IServiceCollection services,
    IConfiguration config,
    string baseDir)
{
    // Domain and Infrastructure layers
    services.AddDomain();
    services.AddInfrastructure(baseDir);

    // Application layer
    services.AddApplication();

    // Host layer implementations
    // ... rest of Host registrations

    return services;
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application/ServiceCollectionExtensions.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(app): add Application layer DI registration"
```

---

## Task 3.8: Create Domain Layer DI Registration

**Files:**
- Create: `SmallEBot.Domain/ServiceCollectionExtensions.cs`

**Step 1: Create Domain layer DI registration**

```csharp
// SmallEBot.Domain/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Domain;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        // Domain layer typically has no implementations, only interfaces
        // Implementations are provided by Infrastructure or Host layers

        return services;
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/ServiceCollectionExtensions.cs
git commit -m "feat(domain): add Domain layer DI registration placeholder"
```

---

## Task 3.9: Clean Up Core Layer - Remove Duplicates

**Files:**
- Delete: `SmallEBot.Core/Entities/Conversation.cs`
- Delete: `SmallEBot.Core/Models/ConversationMetadata.cs`
- Delete: `SmallEBot.Core/Models/TurnMetadata.cs`
- Delete: `SmallEBot.Core/Models/ContextUsageEstimate.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs` - update usings

**Step 1: Identify all files using Core duplicates**

Run: `grep -r "SmallEBot.Core.Entities.Conversation" --include="*.cs"`
Run: `grep -r "SmallEBot.Core.Models.ConversationMetadata" --include="*.cs"`

**Step 2: Update references to use Domain entities**

Update `AgentConversationService.cs`:
```csharp
// Change from
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

// To
using ConversationEntity = SmallEBot.Domain.Conversations.ConversationMetadata;
```

**Step 3: Delete Core duplicates**

```bash
rm SmallEBot.Core/Entities/Conversation.cs
rm SmallEBot.Core/Models/ConversationMetadata.cs
rm SmallEBot.Core/Models/TurnMetadata.cs
rm SmallEBot.Core/Models/ContextUsageEstimate.cs
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded (may need more fixes)

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove Core layer duplicates, use Domain entities"
```

---

## Task 3.10: Update Blazor Components to Use Application Interfaces

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs` (code-behind if exists)

**Step 1: Update ChatArea.razor injections**

```razor
@inject IAgentConversationService ConversationPipeline
@inject IAgentRunner AgentRunner
@inject IWorkspaceUploadService UploadService
@inject IContextUsageEstimator ContextUsageEstimator
@inject IAgentConfigService AgentConfigService
@inject ISkillsConfigService SkillsConfigService
@inject ChatPresentationService Presentation
```

**Step 2: Remove direct dependencies on Host implementations**

- Replace `AgentCacheService` with `IContextUsageEstimator`
- Replace `UserNameService` with `IUserNameProvider` (if needed)

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Components/
git commit -m "refactor(ui): use Application layer interfaces instead of Host implementations"
```

---

## Phase 3 Summary

After Phase 3 completion:

```
SmallEBot.Domain/
├── Common/Services/
│   └── ITokenizer.cs
├── Conversations/Services/
│   ├── ICompressionService.cs
│   └── IContextWindowEstimator.cs
└── ServiceCollectionExtensions.cs

SmallEBot.Application/
├── Agents/
│   ├── IAgentConfigService.cs
│   ├── ISkillsConfigService.cs
│   └── IModelConfigService.cs
├── Context/
│   ├── IContextWindowManager.cs
│   └── ContextWindowManager.cs
├── Conversation/
│   ├── IAgentConversationService.cs
│   ├── AgentConversationService.cs
│   ├── ICompressionService.cs (deprecated)
│   └── IContextUsageEstimator.cs
├── Workspace/
│   └── IWorkspaceUploadService.cs
└── ServiceCollectionExtensions.cs
```

**Dependency Flow:**
```
Blazor UI → Application Interfaces → Application Services → Domain Interfaces
                                    ↓
                            Infrastructure (Repositories)
                                    ↓
                            Host Implementations
```

---

## Verification Checklist

After completing Phase 3:

- [ ] `dotnet build` succeeds with no warnings
- [ ] No duplicate interface definitions (ICompressionService, IContextUsageEstimator)
- [ ] Blazor components depend only on Application layer interfaces
- [ ] Domain layer has no dependencies on other layers
- [ ] Application layer depends only on Domain layer
- [ ] Infrastructure layer depends only on Domain layer
- [ ] Host layer depends on all layers

---

**Phase 3 Complete!** Next: Phase 4 (Host Layer Refactoring - optional cleanup and optimization)
