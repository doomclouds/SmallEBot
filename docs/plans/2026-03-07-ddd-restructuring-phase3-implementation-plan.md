# SmallEBot DDD Restructuring - Phase 3: Application Layer

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Establish complete DDD architecture with Domain services containing business logic and Application services coordinating use cases, decoupling Blazor UI from Host implementations.

**Architecture:** Four-layer DDD approach - (1) Domain: pure C# with business entities, domain service interfaces (no external dependencies), (2) Application: use case orchestration services (depends on Domain, may use AI abstractions), (3) Infrastructure: repository implementations (depends on Domain), (4) Host: Blazor UI and DI configuration (depends on all layers).

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Extensions.AI, Microsoft.Extensions.DependencyInjection

---

## DDD Layer Responsibilities

```
┌─────────────────────────────────────────────────────────────┐
│                    Host (SmallEBot)                            │
│  - Blazor Components (.razor)                               │
│  - Program.cs / DI Configuration                               │
│  - Blazor-specific services (Circuit, JS interop)            │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                 Application (SmallEBot.Application)             │
│  - Use Case Services (IAgentConversationService)              │
│  - AI-dependent interfaces (ICompressionService)             │
│  - DTOs for UI communication                                   │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                Infrastructure (SmallEBot.Infrastructure)        │
│  - Repository Implementations                                   │
│  - File Storage (JsonFileStorage)                               │
│  - AgentSession Serialization                                  │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                    Domain (SmallEBot.Domain)                    │
│  - Business Entities (ConversationMetadata, AgentConfig)     │
│  - Value Objects (ContextUsageEstimate)                          │
│  - Repository Interfaces (IConversationMetadataRepository)   │
│  - Domain Service Interfaces (ITokenizer)                      │
│  - NO EXTERNAL DEPENDENCIES (pure C#)                            │
└─────────────────────────────────────────────────────────────┘
```

**Key Principle: Domain layer has NO dependencies on Microsoft.Extensions.AI, Blazor, or any framework.**

---

## Task 3.1: Establish Service Interface Boundaries

**Files:**
- Read: `SmallEBot.Application/Conversation/ICompressionService.cs`
- Read: `SmallEBot.Application/Conversation/IContextUsageEstimator.cs`
- Read: `SmallEBot.Domain/Conversations/Services/ICompressionService.cs`
- Read: `SmallEBot.Domain/Conversations/Services/IContextWindowEstimator.cs`

**Analysis:**

| Interface | Current Location | Uses AI Types? | Correct Layer |
|----------|-----------------|--------------|----------------|
| `ICompressionService` | Domain + Application | Yes (`ChatMessage`) | Application |
| `IContextUsageEstimator` | Application | Yes (`ChatMessage`) | Application |
| `IContextWindowEstimator` | Domain | No | Domain |
| `ITokenizer` | Host | No | Domain |

**Decision:**
1. **Domain layer** interfaces should have NO external dependencies
   - Keep `IContextWindowEstimator` in Domain (it only uses `Guid` and primitive types)
   - Add `ITokenizer` to Domain

2. **Application layer** interfaces can depend on AI abstractions
   - Move `ICompressionService` to Application only (it uses `ChatMessage`)
   - Move `IContextUsageEstimator` to Application only

**Step 1: Delete duplicate ICompressionService from Domain layer**

The Domain layer's `ICompressionService` uses `ChatMessage` from `Microsoft.Extensions.AI`. This is incorrect - Domain should have no external dependencies.

Delete: `SmallEBot.Domain/Conversations/Services/ICompressionService.cs`

**Step 2: Keep Application layer's ICompressionService**

The Application layer's `ICompressionService` is correct - it uses `ChatMessage` which is acceptable for Application layer.

**Step 3: Update IContextUsageEstimator to use Domain's ContextUsageEstimate**

Update `SmallEBot.Application/Conversation/IContextUsageEstimator.cs`:

```csharp
// SmallEBot.Application/Conversation/IContextUsageEstimator.cs
using SmallEBot.Domain.Conversations.Services;

namespace SmallEBot.Application.Conversation;

/// <summary>
/// Provides context usage estimation for compression threshold checking.
/// Uses Domain's ContextUsageEstimate record.
/// </summary>
public interface IContextUsageEstimator
{
    /// <summary>
    /// Get detailed context usage estimate including ratio, used tokens, and context window size.
    /// </summary>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Domain/Conversations/Services/ICompressionService.cs SmallEBot.Application/Conversation/IContextUsageEstimator.cs
git commit -m "refactor: establish correct service interface layer boundaries"
```

---

## Task 3.2: Add ITokenizer Interface to Domain Layer

**Files:**
- Create: `SmallEBot.Domain/Common/Services/ITokenizer.cs`

**Step 1: Create ITokenizer interface in Domain**

```csharp
// SmallEBot.Domain/Common/Services/ITokenizer.cs
namespace SmallEBot.Domain.Common.Services;

/// <summary>
/// Tokenizer for counting tokens in text.
/// Used for context window estimation and compression.
/// Pure domain interface with no external dependencies.
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

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Common/Services/ITokenizer.cs
git commit -m "feat(domain): add ITokenizer interface for token counting"
```

---

## Task 3.3: Create Domain Services for Business Logic

**Files:**
- Create: `SmallEBot.Domain/Conversations/Services/IConversationTitleGenerator.cs`
- Create: `SmallEBot.Domain/Agents/Services/IContextBuilder.cs`

**Step 1: Create IConversationTitleGenerator interface**

```csharp
// SmallEBot.Domain/Conversations/Services/IConversationTitleGenerator.cs
namespace SmallEBot.Domain.Conversations.Services;

/// <summary>
/// Generates titles for conversations based on content.
/// Pure domain service interface.
/// </summary>
public interface IConversationTitleGenerator
{
    /// <summary>
    /// Generates a title for a conversation based on the first user message.
    /// </summary>
    /// <param name="firstUserMessage">The first user message in the conversation.</param>
    /// <returns>Generated title, or null if generation failed.</returns>
    Task<string?> GenerateTitleAsync(string firstUserMessage, CancellationToken ct = default);
}
```

**Step 2: Create IContextBuilder interface**

```csharp
// SmallEBot.Domain/Agents/Services/IContextBuilder.cs
namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Builds context for agent interactions.
/// Pure domain service interface.
/// </summary>
public interface IContextBuilder
{
    /// <summary>
    /// Gets the system prompt for the given agent configuration.
    /// </summary>
    Task<string> BuildSystemPromptAsync(
        string agentId,
        string? compressedContext,
        CancellationToken ct = default);
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Domain/Conversations/Services/IConversationTitleGenerator.cs SmallEBot.Domain/Agents/Services/IContextBuilder.cs
git commit -m "feat(domain): add domain service interfaces for business logic"
```

---

## Task 3.4: Move Tokenizer Implementations to Infrastructure Layer

**Files:**
- Create: `SmallEBot.Infrastructure/Services/DeepSeekTokenizer.cs`
- Create: `SmallEBot.Infrastructure/Services/CharEstimateTokenizer.cs`
- Modify: `SmallEBot/Services/Agent/Tokenizer.cs` (keep as backward-compatible wrapper)

**Step 1: Create DeepSeekTokenizer in Infrastructure**

```csharp
// SmallEBot.Infrastructure/Services/DeepSeekTokenizer.cs
using System.Diagnostics.CodeAnalysis;
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Tokenizer using DeepSeek's tokenization algorithm.
/// Implements ITokenizer interface from Domain layer.
/// </summary>
public sealed class DeepSeekTokenizer : ITokenizer, IDisposable
{
    private readonly object? _tokenizer;
    private bool _disposed;

    public DeepSeekTokenizer(string vocabularyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vocabularyPath, nameof(vocabularyPath));

        if (!File.Exists(vocabularyPath))
            throw new FileNotFoundException($"Tokenizer vocabulary not found: {vocabularyPath}");

        // Initialize tokenizer with vocabulary file
        // _tokenizer = ...;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Use tokenizer to count tokens
        // return _tokenizer.Encode(text).Count;

        // Fallback implementation
        return text.Length / 4;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // _tokenizer?.Dispose();
    }
}
```

**Step 2: Create CharEstimateTokenizer in Infrastructure**

```csharp
// SmallEBot.Infrastructure/Services/CharEstimateTokenizer.cs
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Simple tokenizer that estimates tokens based on character count.
/// Fallback implementation when no vocabulary file is available.
/// </summary>
public sealed class CharEstimateTokenizer : ITokenizer
{
    private const int CharsPerToken = 4;

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / CharsPerToken;
    }
}
```

**Step 3: Update DI registration in Infrastructure**

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
// Add to AddInfrastructure method:

// Tokenizer - choose based on configuration
services.AddSingleton<ITokenizer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var basePath = sp.GetRequiredService<string>(); // or however basePath is passed

    var tokenizerPath = config["Anthropic:TokenizerPath"];

    if (!string.IsNullOrEmpty(tokenizerPath) && File.Exists(tokenizerPath))
    {
        return new DeepSeekTokenizer(tokenizerPath);
    }

    return new CharEstimateTokenizer();
});
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Infrastructure/Services/ SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(infra): add ITokenizer implementations"
```

---

## Task 3.5: Move CompressionService to Infrastructure Layer

**Files:**
- Create: `SmallEBot.Infrastructure/Services/CompressionService.cs`
- Delete: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 1: Create CompressionService in Infrastructure**

```csharp
// SmallEBot.Infrastructure/Services/CompressionService.cs
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Compresses conversation history by calling LLM with compact skill prompt.
/// Implements ICompressionService from Application layer.
/// Depends on IChatClient for LLM calls.
/// </summary>
public sealed class CompressionService : ICompressionService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<CompressionService> _logger;

    private const string CompactPrompt = """
You are compressing conversation history to save context space.

## Input
You will receive:
1. Previous summary (if exists) - already compressed content
2. New conversation messages to compress

## Task
Generate a MERGED summary that combines the previous summary with the new messages.
Preserve all important information, update state as needed.

## Format
Use this compact format:

## Summary
[1-2 sentences overview]

## Decisions
- [decision]: [reasoning]

## Files
- path/to/file: [change summary]

## State
- Done: [items]
- Pending: [items]

## Context
- [key=value pairs or important notes]

Keep total output under 800 tokens. Focus on what's needed to continue the work.
""";

    public CompressionService(IChatClient chatClient, ILogger<CompressionService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default)
    {
        if (messages.Count == 0 && string.IsNullOrEmpty(existingSummary))
            return existingSummary;

        var sb = new StringBuilder();

        // Include existing summary if present
        if (!string.IsNullOrEmpty(existingSummary))
        {
            sb.AppendLine("## Previous Summary (merge with new messages)");
            sb.AppendLine(existingSummary);
            sb.AppendLine();
        }

        if (messages.Count > 0)
        {
            sb.AppendLine("## New Messages to Compress");
            sb.AppendLine();

            foreach (var message in messages)
            {
                var role = message.Role == ChatRole.User ? "User" : "Assistant";
                sb.AppendLine($"[{role}]:");

                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        sb.AppendLine(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoning)
                    {
                        var reasoningPreview = reasoning.Text.Length > 200
                            ? reasoning.Text[..200] + "..."
                            : reasoning.Text;
                        sb.AppendLine($"[Thinking]: {reasoningPreview}");
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        sb.AppendLine($"[Tool: {fnCall.Name}]");
                        sb.AppendLine($"Arguments: {ToJsonString(fnCall.Arguments)}");
                    }
                    else if (content is FunctionResultContent fnResult)
                    {
                        var result = TruncateResult(fnResult.Result?.ToString(), toolResultMaxLength);
                        sb.AppendLine($"[Tool Result]: {result}");
                    }
                }

                sb.AppendLine();
            }
        }

        try
        {
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, CompactPrompt),
                new(ChatRole.User, sb.ToString())
            };

            var response = await _chatClient.CompleteAsync(chatMessages, cancellationToken: ct);
            _logger.LogInformation("Compression generated summary: {Length} chars", response.Message.Text?.Length ?? 0);
            return response.Message.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate compression summary");
            return null;
        }
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    private static string ToJsonString(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(arguments);
    }
}
```

**Step 2: Update DI registration in Infrastructure**

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
// Add to AddInfrastructure method:

// Compression service - depends on IChatClient
services.AddScoped<ICompressionService, CompressionService>();
```

**Step 3: Delete old CompressionService from Host**

Delete: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 4: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Infrastructure/Services/CompressionService.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git rm SmallEBot/Services/Agent/CompressionService.cs
git commit -m "refactor: move CompressionService to Infrastructure layer"
```

---

## Task 3.6: Move ContextWindowManager to Application Layer

**Files:**
- Create: `SmallEBot.Application/Context/ContextWindowManager.cs`
- Delete: `SmallEBot/Services/Context/ContextWindowManager.cs`

**Step 1: Update IContextWindowManager interface**

```csharp
// SmallEBot.Application/Context/IContextWindowManager.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Context;

/// <summary>
/// Result of trimming messages to fit context window.
/// </summary>
public record TrimResult(IReadOnlyList<ChatMessage> Messages, int TokenCount, int TrimmedCount);

/// <summary>
/// Manages context window for conversations.
/// Application layer interface - can depend on AI abstractions.
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

**Step 2: Create ContextWindowManager in Application**

```csharp
// SmallEBot.Application/Context/ContextWindowManager.cs
using Microsoft.Extensions.AI;
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Application.Context;

/// <summary>
/// Manages context window using tokenizer for estimation.
/// Implements IContextWindowManager from Application layer.
/// Depends on ITokenizer from Domain layer.
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
```

**Step 3: Update DI registration in Application**

```csharp
// SmallEBot.Application/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Application.Context;
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Context management - depends on ITokenizer from Domain
        services.AddScoped<IContextWindowManager, ContextWindowManager>();

        return services;
    }
}
```

**Step 4: Delete old ContextWindowManager from Host**

Delete: `SmallEBot/Services/Context/ContextWindowManager.cs`

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Application/Context/ SmallEBot.Application/ServiceCollectionExtensions.cs
git rm SmallEBot/Services/Context/ContextWindowManager.cs
git commit -m "refactor: move ContextWindowManager to Application layer"
```

---

## Task 3.7: Create Application Service Contracts for Blazor UI

**Files:**
- Create: `SmallEBot.Application/Agents/IAgentConfigService.cs`
- Create: `SmallEBot.Application/Agents/ISkillsConfigService.cs`
- Create: `SmallEBot.Application/Agents/IModelConfigService.cs`
- Create: `SmallEBot.Application/Workspace/IWorkspaceUploadService.cs`
- Create: `SmallEBot.Application/User/IUserNameProvider.cs`

**Step 1: Create IAgentConfigService**

```csharp
// SmallEBot.Application/Agents/IAgentConfigService.cs
using SmallEBot.Domain.Agents;

namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing agent configurations.
/// Blazor UI depends on this abstraction, not Host implementations.
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

**Step 2: Create ISkillsConfigService**

```csharp
// SmallEBot.Application/Agents/ISkillsConfigService.cs
namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing skills configuration.
/// Blazor UI depends on this abstraction.
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

    /// <summary>
    /// Invalidates the skill cache.
    /// </summary>
    Task InvalidateCacheAsync(CancellationToken ct = default);
}
```

**Step 3: Create IModelConfigService**

```csharp
// SmallEBot.Application/Agents/IModelConfigService.cs
using SmallEBot.Domain.Agents.ValueObjects;

namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing model configurations.
/// Blazor UI depends on this abstraction.
/// </summary>
public interface IModelConfigService
{
    /// <summary>
    /// Gets all available model configurations.
    /// </summary>
    Task<IReadOnlyList<ModelConfig>> GetAllModelConfigsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a model configuration by ID.
    /// </summary>
    Task<ModelConfig?> GetModelConfigAsync(string id, CancellationToken ct = default);
}
```

**Step 4: Create IWorkspaceUploadService**

```csharp
// SmallEBot.Application/Workspace/IWorkspaceUploadService.cs
namespace SmallEBot.Application.Workspace;

/// <summary>
/// Application service for workspace file uploads.
/// Blazor UI depends on this abstraction.
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

**Step 5: Create IUserNameProvider**

```csharp
// SmallEBot.Application/User/IUserNameProvider.cs
namespace SmallEBot.Application.User;

/// <summary>
/// Provides the current user name.
/// Blazor UI depends on this abstraction.
/// </summary>
public interface IUserNameProvider
{
    /// <summary>
    /// Gets the current user name.
    /// </summary>
    string UserName { get; }
}
```

**Step 6: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add SmallEBot.Application/Agents/ SmallEBot.Application/Workspace/ SmallEBot.Application/User/
git commit -m "feat(app): add application service contracts for Blazor UI"
```

---

## Task 3.8: Refactor Host Services to Implement Application Interfaces

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentConfigService.cs`
- Modify: `SmallEBot/Services/Skills/SkillsConfigService.cs`
- Modify: `SmallEBot/Services/Agent/ModelConfigService.cs`
- Modify: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs`
- Modify: `SmallEBot/Services/User/UserNameService.cs`
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
        // Could be from configuration or user preferences
        return 2000;
    }

    public double GetCompressionThreshold()
    {
        // Could be from configuration or user preferences
        return 0.8;
    }
}
```

**Step 2: Update SkillsConfigService**

```csharp
// SmallEBot/Services/Skills/SkillsConfigService.cs
using SmallEBot.Application.Agents;

namespace SmallEBot.Services.Skills;

public class SkillsConfigService : ISkillsConfigService
{
    // ... existing implementation, updated to implement interface
}
```

**Step 3: Update ModelConfigService**

```csharp
// SmallEBot/Services/Agent/ModelConfigService.cs
using SmallEBot.Application.Agents;

namespace SmallEBot.Services.Agent;

public class ModelConfigService : IModelConfigService
{
    // ... existing implementation, updated to implement interface
}
```

**Step 4: Update WorkspaceUploadService**

```csharp
// SmallEBot/Services/Workspace/WorkspaceUploadService.cs
using SmallEBot.Application.Workspace;

namespace SmallEBot.Services.Workspace;

public class WorkspaceUploadService : IWorkspaceUploadService
{
    // ... existing implementation, updated to implement interface
}
```

**Step 5: Update UserNameService**

```csharp
// SmallEBot/Services/User/UserNameService.cs
using SmallEBot.Application.User;

namespace SmallEBot.Services.User;

public class UserNameService : IUserNameProvider
{
    public string UserName { get; private set; } = "DefaultUser";

    // ... existing implementation
}
```

**Step 6: Update DI registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
// Add Application interfaces registration with Host implementations:

// Register Host implementations for Application interfaces
services.AddScoped<IAgentConfigService, AgentConfigService>();
services.AddScoped<ISkillsConfigService, SkillsConfigService>();
services.AddScoped<IModelConfigService, ModelConfigService>();
services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();
services.AddScoped<IUserNameProvider, UserNameService>();
```

**Step 7: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add SmallEBot/Services/ SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(host): implement Application service interfaces"
```

---

## Task 3.9: Update Blazor Components to Use Application Interfaces

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Modify: Any other components using Host services directly

**Step 1: Update ChatArea.razor injections**

```razor
@inject IAgentConversationService ConversationPipeline
@inject IAgentRunner AgentRunner
@inject IWorkspaceUploadService UploadService
@inject IContextUsageEstimator ContextUsageEstimator
@inject IAgentConfigService AgentConfigService
@inject ISkillsConfigService SkillsConfigService
@inject IUserNameProvider UserNameProvider
@inject ChatPresentationService Presentation
```

**Step 2: Update code-behind or @code block to use interfaces**

Replace any direct usage of:
- `AgentCacheService` → `IContextUsageEstimator`
- `UserNameService` → `IUserNameProvider`

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Components/
git commit -m "refactor(ui): use Application layer interfaces instead of Host implementations"
```

---

## Task 3.10: Clean Up and Final Verification

**Files:**
- Read: `SmallEBot.slnx` - verify project references
- Read: All project files - verify dependency direction

**Step 1: Verify dependency direction**

Run: `dotnet build`
Expected: Build succeeded

**Step 2: Verify no circular dependencies**

Check that:
- Domain has NO project references (only NuGet packages)
- Application references Domain only
- Infrastructure references Domain only
- Host references Application, Domain, Infrastructure

**Step 3: Run application to verify**

Run: `dotnet run --project SmallEBot`
Expected: Application starts without errors

**Step 4: Final commit**

```bash
git add -A
git commit -m "refactor: complete Phase 3 DDD restructuring - Application Layer"
```

---

## Phase 3 Summary

After Phase 3 completion:

```
SmallEBot.Domain/
├── Common/Services/
│   └── ITokenizer.cs
├── Conversations/Services/
│   ├── IContextWindowEstimator.cs
│   └── ContextUsageEstimate.cs
├── Agents/Services/
│   └── IContextBuilder.cs
└── (no external dependencies - pure C#)

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
│   ├── ICompressionService.cs
│   └── IContextUsageEstimator.cs
├── Workspace/
│   └── IWorkspaceUploadService.cs
├── User/
│   └── IUserNameProvider.cs
└── ServiceCollectionExtensions.cs

SmallEBot.Infrastructure/
├── Services/
│   ├── CompressionService.cs
│   ├── DeepSeekTokenizer.cs
│   └── CharEstimateTokenizer.cs
├── Persistence/
│   └── (existing repositories)
└── ServiceCollectionExtensions.cs
```

**Dependency Flow:**
```
Blazor UI (Host)
    ↓
Application Interfaces
    ↓
Application Services → Domain Interfaces
    ↓
Infrastructure (Repositories + Services)
    ↓
Domain (Entities + Value Objects)
```

**Key Principle: Domain layer has NO dependencies on Microsoft.Extensions.AI, Blazor, or any framework.**

---

**Phase 3 Complete!**
