# SmallEBot DDD Restructuring - Phase 3: Application Layer

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Establish complete DDD architecture with separate Application.Contracts project for interfaces, decoupling Blazor UI from implementations.

**Architecture:** Four-layer DDD approach - (1) Domain: pure C# with business entities, domain service interfaces (no external dependencies), (2) Application.Contracts: pure interface definitions (no implementations), (3) Application: use case implementations, (4) Infrastructure: repository implementations, (5) Host: Blazor UI and DI configuration.

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
│              Application (SmallEBot.Application)              │
│  - Use Case Implementations                                  │
│  - AgentConversationService, ContextWindowManager            │
│  - Depends on Contracts + Domain                              │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│       Application.Contracts (SmallEBot.Application.Contracts)│
│  - Interface definitions ONLY (no implementations)           │
│  - ICompressionService, IContextUsageEstimator, etc.          │
│  - Can reference Domain + ME.AI abstractions                  │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                Infrastructure (SmallEBot.Infrastructure)        │
│  - Repository Implementations                                   │
│  - CompressionService, Tokenizer implementations               │
│  - File Storage (JsonFileStorage)                               │
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

**Key Principle:**
- Domain layer has NO dependencies on Microsoft.Extensions.AI, Blazor, or any framework
- Application.Contracts has only interfaces, no implementations

---

## Task 3.1: Create Application.Contracts Project

**Files:**
- Create: `SmallEBot.Application.Contracts/SmallEBot.Application.Contracts.csproj`
- Modify: `SmallEBot.slnx`

**Step 1: Create project file**

```xml
<!-- SmallEBot.Application.Contracts/SmallEBot.Application.Contracts.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SmallEBot.Application</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SmallEBot.Domain\SmallEBot.Domain.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Add to solution**

Run: `dotnet sln add SmallEBot.Application.Contracts/SmallEBot.Application.Contracts.csproj`

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Application.Contracts`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/ SmallEBot.slnx
git commit -m "feat(app): create Application.Contracts project for interfaces"
```

---

## Task 3.2: Move Conversation Interfaces to Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversation/ICompressionService.cs`
- Create: `SmallEBot.Application.Contracts/Conversation/IContextUsageEstimator.cs`
- Create: `SmallEBot.Application.Contracts/Conversation/IAgentConversationService.cs`
- Delete: `SmallEBot.Application/Conversation/ICompressionService.cs`
- Delete: `SmallEBot.Application/Conversation/IContextUsageEstimator.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Create ICompressionService in Contracts**

```csharp
// SmallEBot.Application.Contracts/Conversation/ICompressionService.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Conversation;

/// <summary>
/// Service for compressing conversation context using LLM.
/// Application layer interface - can depend on AI abstractions.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// Generates a compressed summary from messages, optionally merging with existing summary.
    /// </summary>
    /// <param name="messages">Messages to summarize.</param>
    /// <param name="toolResultMaxLength">Max length for tool results in the summary.</param>
    /// <param name="existingSummary">Existing compressed context to merge with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default);
}
```

**Step 2: Create IContextUsageEstimator in Contracts**

```csharp
// SmallEBot.Application.Contracts/Conversation/IContextUsageEstimator.cs
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

**Step 3: Create IAgentConversationService in Contracts**

```csharp
// SmallEBot.Application.Contracts/Conversation/IAgentConversationService.cs
using SmallEBot.Application.Streaming;
using SmallEBot.Domain.Conversations;

namespace SmallEBot.Application.Conversation;

/// <summary>
/// Application service for managing agent conversations.
/// Orchestrates conversation lifecycle, turn management, and streaming responses.
/// </summary>
public interface IAgentConversationService
{
    /// <summary>
    /// Event raised when compression starts for a conversation.
    /// </summary>
    event Action<Guid>? CompressionStarted;

    /// <summary>
    /// Event raised when compression completes for a conversation.
    /// </summary>
    event Action<Guid, bool>? CompressionCompleted;

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all conversations for a user.
    /// </summary>
    Task<List<ConversationMetadata>> GetConversationsAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<List<ConversationMetadata>> SearchConversationsAsync(
        string userName,
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific conversation.
    /// </summary>
    Task<ConversationMetadata?> GetConversationAsync(
        Guid id,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a conversation.
    /// </summary>
    Task<bool> DeleteConversationAsync(
        Guid id,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a turn and user message, then streams the agent response.
    /// </summary>
    Task StreamResponseAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>
    /// Regenerates the response for a specific turn.
    /// </summary>
    Task RegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>
    /// Checks context usage and compresses if needed.
    /// </summary>
    Task<bool> CheckAndCompactIfNeededAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Manually triggers compression for a conversation.
    /// </summary>
    Task<bool> CompactConversationAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
```

**Step 4: Delete old interfaces from Application project**

Delete:
- `SmallEBot.Application/Conversation/ICompressionService.cs`
- `SmallEBot.Application/Conversation/IContextUsageEstimator.cs`

**Step 5: Update AgentConversationService to use Contracts interfaces**

```csharp
// SmallEBot.Application/Conversation/AgentConversationService.cs
// Update usings to reference Contracts namespace
// The class now IMPLEMENTS the interface from Contracts
```

**Step 6: Add project reference to Application.csproj**

```xml
<!-- SmallEBot.Application/SmallEBot.Application.csproj -->
<ItemGroup>
  <ProjectReference Include="..\SmallEBot.Application.Contracts\SmallEBot.Application.Contracts.csproj" />
</ItemGroup>
```

**Step 7: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversation/ SmallEBot.Application/
git commit -m "refactor: move conversation interfaces to Application.Contracts"
```

---

## Task 3.3: Move Session Interfaces to Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Session/ISessionFileService.cs`
- Create: `SmallEBot.Application.Contracts/Session/ISessionManager.cs`
- Create: `SmallEBot.Application.Contracts/Session/IAgentSessionReader.cs`
- Delete: `SmallEBot.Application/Session/ISessionFileService.cs`
- Delete: `SmallEBot.Application/Session/ISessionManager.cs`
- Delete: `SmallEBot.Application/Session/IAgentSessionReader.cs`

**Step 1: Create ISessionFileService in Contracts**

```csharp
// SmallEBot.Application.Contracts/Session/ISessionFileService.cs
using SmallEBot.Domain.Conversations;

namespace SmallEBot.Application.Session;

/// <summary>
/// Service for managing conversation session files.
/// </summary>
public interface ISessionFileService
{
    /// <summary>
    /// Loads conversation metadata by ID.
    /// </summary>
    Task<ConversationMetadata?> LoadAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Saves conversation metadata.
    /// </summary>
    Task SaveAsync(
        ConversationMetadata metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation.
    /// </summary>
    Task DeleteAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all conversations for a user.
    /// </summary>
    Task<IReadOnlyList<ConversationMetadata>> ListAsync(
        string userName,
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<IReadOnlyList<ConversationMetadata>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default);
}
```

**Step 2: Create ISessionManager in Contracts**

```csharp
// SmallEBot.Application.Contracts/Session/ISessionManager.cs
using SmallEBot.Domain.Conversations;

namespace SmallEBot.Application.Session;

/// <summary>
/// Manages conversation sessions and agent state persistence.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent session for a conversation.
    /// </summary>
    Task<Microsoft.Agents.AI.AgentSession?> GetSessionAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Persists the current session state.
    /// </summary>
    Task PersistSessionAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
```

**Step 3: Create IAgentSessionReader in Contracts**

```csharp
// SmallEBot.Application.Contracts/Session/IAgentSessionReader.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Session;

/// <summary>
/// Reads messages and content from agent sessions.
/// </summary>
public interface IAgentSessionReader
{
    /// <summary>
    /// Gets all messages from a conversation's agent session.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the user message content at a specific turn index.
    /// </summary>
    Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int turnIndex,
        CancellationToken ct = default);
}
```

**Step 4: Delete old interfaces**

Delete:
- `SmallEBot.Application/Session/ISessionFileService.cs`
- `SmallEBot.Application/Session/ISessionManager.cs`
- `SmallEBot.Application/Session/IAgentSessionReader.cs`

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Application.Contracts/Session/ SmallEBot.Application/Session/
git commit -m "refactor: move session interfaces to Application.Contracts"
```

---

## Task 3.4: Move Streaming Interfaces to Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Streaming/IAgentRunner.cs`
- Create: `SmallEBot.Application.Contracts/Streaming/IStreamSink.cs`
- Delete: `SmallEBot.Application/Streaming/IAgentRunner.cs`
- Delete: `SmallEBot.Application/Streaming/IStreamSink.cs`

**Step 1: Create IAgentRunner in Contracts**

```csharp
// SmallEBot.Application.Contracts/Streaming/IAgentRunner.cs
namespace SmallEBot.Application.Streaming;

/// <summary>
/// Runs agent interactions and produces streaming updates.
/// </summary>
public interface IAgentRunner
{
    /// <summary>
    /// Runs the agent with the given user message and streams updates.
    /// </summary>
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>
    /// Generates a title for a conversation based on the first user message.
    /// </summary>
    Task<string> GenerateTitleAsync(
        string firstUserMessage,
        CancellationToken cancellationToken = default);
}
```

**Step 2: Create IStreamSink in Contracts**

```csharp
// SmallEBot.Application.Contracts/Streaming/IStreamSink.cs
namespace SmallEBot.Application.Streaming;

/// <summary>
/// Receives streaming updates from agent execution.
/// </summary>
public interface IStreamSink
{
    /// <summary>
    /// Called when a new stream update is available.
    /// </summary>
    Task OnNextAsync(StreamUpdate update, CancellationToken cancellationToken = default);
}
```

**Step 3: Create StreamUpdate model in Contracts**

```csharp
// SmallEBot.Application.Contracts/Streaming/StreamUpdate.cs
namespace SmallEBot.Application.Streaming;

/// <summary>
/// Represents a streaming update from agent execution.
/// </summary>
public record StreamUpdate
{
    public StreamUpdateType Type { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgs { get; init; }
    public string? ToolResult { get; init; }
    public string? Thinking { get; init; }
    public string? Error { get; init; }
    public Guid? ApprovalId { get; init; }
    public string? ApprovalMessage { get; init; }
    public string? ApprovalContext { get; init; }
}

public enum StreamUpdateType
{
    Text,
    Thinking,
    ToolCall,
    ToolResult,
    Error,
    ApprovalRequest,
    ApprovalResult
}
```

**Step 4: Delete old interfaces**

Delete:
- `SmallEBot.Application/Streaming/IAgentRunner.cs`
- `SmallEBot.Application/Streaming/IStreamSink.cs`

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Application.Contracts/Streaming/ SmallEBot.Application/Streaming/
git commit -m "refactor: move streaming interfaces to Application.Contracts"
```

---

## Task 3.5: Move Context Interfaces to Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Context/IContextWindowManager.cs`
- Delete: `SmallEBot.Application/Context/IContextWindowManager.cs`

**Step 1: Create IContextWindowManager in Contracts**

```csharp
// SmallEBot.Application.Contracts/Context/IContextWindowManager.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Context;

/// <summary>
/// Result of trimming messages to fit context window.
/// </summary>
public record TrimResult(IReadOnlyList<ChatMessage> Messages, int TokenCount, int TrimmedCount);

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

**Step 2: Delete old interface**

Delete: `SmallEBot.Application/Context/IContextWindowManager.cs`

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Context/ SmallEBot.Application/Context/
git commit -m "refactor: move context interfaces to Application.Contracts"
```

---

## Task 3.6: Create Agent/Workspace/User Service Interfaces in Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Agents/IAgentConfigService.cs`
- Create: `SmallEBot.Application.Contracts/Agents/ISkillsConfigService.cs`
- Create: `SmallEBot.Application.Contracts/Agents/IModelConfigService.cs`
- Create: `SmallEBot.Application.Contracts/Workspace/IWorkspaceUploadService.cs`
- Create: `SmallEBot.Application.Contracts/User/IUserNameProvider.cs`

**Step 1: Create IAgentConfigService**

```csharp
// SmallEBot.Application.Contracts/Agents/IAgentConfigService.cs
using SmallEBot.Domain.Agents;

namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing agent configurations.
/// Blazor UI depends on this abstraction.
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
// SmallEBot.Application.Contracts/Agents/ISkillsConfigService.cs
namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing skills configuration.
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
// SmallEBot.Application.Contracts/Agents/IModelConfigService.cs
using SmallEBot.Domain.Agents.ValueObjects;

namespace SmallEBot.Application.Agents;

/// <summary>
/// Application service for managing model configurations.
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
// SmallEBot.Application.Contracts/Workspace/IWorkspaceUploadService.cs
namespace SmallEBot.Application.Workspace;

/// <summary>
/// Application service for workspace file uploads.
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
// SmallEBot.Application.Contracts/User/IUserNameProvider.cs
namespace SmallEBot.Application.User;

/// <summary>
/// Provides the current user name.
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

Run: `dotnet build SmallEBot.Application.Contracts`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add SmallEBot.Application.Contracts/Agents/ SmallEBot.Application.Contracts/Workspace/ SmallEBot.Application.Contracts/User/
git commit -m "feat(contracts): add agent/workspace/user service interfaces"
```

---

## Task 3.7: Add ITokenizer Interface to Domain Layer

**Files:**
- Create: `SmallEBot.Domain/Common/Services/ITokenizer.cs`

**Step 1: Create ITokenizer interface**

```csharp
// SmallEBot.Domain/Common/Services/ITokenizer.cs
namespace SmallEBot.Domain.Common.Services;

/// <summary>
/// Tokenizer for counting tokens in text.
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

## Task 3.8: Move Service Implementations to Correct Layers

**Files:**
- Create: `SmallEBot.Infrastructure/Services/CompressionService.cs`
- Create: `SmallEBot.Infrastructure/Services/Tokenizer.cs`
- Delete: `SmallEBot/Services/Agent/CompressionService.cs`
- Modify: `SmallEBot.Application/Context/ContextWindowManager.cs` (if exists in Host)

**Step 1: Move CompressionService to Infrastructure**

```csharp
// SmallEBot.Infrastructure/Services/CompressionService.cs
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Compresses conversation history by calling LLM with compact skill prompt.
/// Implements ICompressionService from Application.Contracts.
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
"";

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
                    switch (content)
                    {
                        case TextContent textContent:
                            sb.AppendLine(textContent.Text);
                            break;
                        case TextReasoningContent reasoning:
                            var reasoningPreview = reasoning.Text.Length > 200
                                ? reasoning.Text[..200] + "..."
                                : reasoning.Text;
                            sb.AppendLine($"[Thinking]: {reasoningPreview}");
                            break;
                        case FunctionCallContent fnCall:
                            sb.AppendLine($"[Tool: {fnCall.Name}]");
                            sb.AppendLine($"Arguments: {ToJsonString(fnCall.Arguments)}");
                            break;
                        case FunctionResultContent fnResult:
                            var result = TruncateResult(fnResult.Result?.ToString(), toolResultMaxLength);
                            sb.AppendLine($"[Tool Result]: {result}");
                            break;
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
            _logger.LogInformation("Compression generated summary: {Length} chars",
                response.Message.Text?.Length ?? 0);
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

**Step 2: Create Tokenizer implementations in Infrastructure**

```csharp
// SmallEBot.Infrastructure/Services/Tokenizer.cs
using SmallEBot.Domain.Common.Services;

namespace SmallEBot.Infrastructure.Services;

/// <summary>
/// Simple tokenizer that estimates tokens based on character count.
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

/// <summary>
/// Tokenizer using DeepSeek's algorithm (if vocabulary available).
/// </summary>
public sealed class DeepSeekTokenizer : ITokenizer, IDisposable
{
    private readonly string _vocabularyPath;
    private bool _disposed;

    public DeepSeekTokenizer(string vocabularyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vocabularyPath, nameof(vocabularyPath));

        if (!File.Exists(vocabularyPath))
            throw new FileNotFoundException($"Tokenizer vocabulary not found: {vocabularyPath}");

        _vocabularyPath = vocabularyPath;
        // Initialize tokenizer with vocabulary
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Actual tokenizer implementation
        // For now, use fallback
        return text.Length / 4;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Cleanup resources
    }
}
```

**Step 3: Update DI registration in Infrastructure**

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
// Add to AddInfrastructure method:

using SmallEBot.Domain.Common.Services;
using SmallEBot.Infrastructure.Services;

// Tokenizer - choose based on configuration
services.AddSingleton<ITokenizer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var basePath = /* get base path */;

    var tokenizerPath = config["Anthropic:TokenizerPath"];

    if (!string.IsNullOrEmpty(tokenizerPath) && File.Exists(tokenizerPath))
    {
        return new DeepSeekTokenizer(tokenizerPath);
    }

    return new CharEstimateTokenizer();
});

// Compression service
services.AddScoped<ICompressionService, CompressionService>();
```

**Step 4: Delete old CompressionService from Host**

Delete: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Infrastructure/Services/ SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git rm SmallEBot/Services/Agent/CompressionService.cs
git commit -m "refactor: move CompressionService and Tokenizer to Infrastructure layer"
```

---

## Task 3.9: Update Host Services to Implement Contracts Interfaces

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentConfigService.cs`
- Modify: `SmallEBot/Services/Skills/SkillsConfigService.cs`
- Modify: `SmallEBot/Services/Agent/ModelConfigService.cs`
- Modify: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs`
- Modify: `SmallEBot/Services/User/UserNameService.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add Contracts project reference to Host**

```xml
<!-- SmallEBot/SmallEBot.csproj -->
<ItemGroup>
  <ProjectReference Include="..\SmallEBot.Application.Contracts\SmallEBot.Application.Contracts.csproj" />
</ItemGroup>
```

**Step 2: Update Host services to implement Contracts interfaces**

Each Host service should now implement the interface from `SmallEBot.Application.Contracts`.

Example for AgentConfigService:
```csharp
// SmallEBot/Services/Agent/AgentConfigService.cs
using SmallEBot.Application.Agents;  // Interface from Contracts
using SmallEBot.Domain.Agents;       // Domain entities

namespace SmallEBot.Services.Agent;

public class AgentConfigService : IAgentConfigService
{
    // Implementation...
}
```

**Step 3: Update DI registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
using SmallEBot.Application.Agents;
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Context;
using SmallEBot.Application.Session;
using SmallEBot.Application.Streaming;
using SmallEBot.Application.User;
using SmallEBot.Application.Workspace;

// Register Contracts interfaces with Host implementations
services.AddScoped<IAgentConfigService, AgentConfigService>();
services.AddScoped<ISkillsConfigService, SkillsConfigService>();
services.AddScoped<IModelConfigService, ModelConfigService>();
services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();
services.AddScoped<IUserNameProvider, UserNameService>();
```

**Step 4: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot/Services/ SmallEBot/Extensions/ServiceCollectionExtensions.cs SmallEBot/SmallEBot.csproj
git commit -m "refactor(host): implement Application.Contracts interfaces"
```

---

## Task 3.10: Update Blazor Components and Final Verification

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: All Blazor components using Host services directly
- Modify: `SmallEBot.slnx`

**Step 1: Update ChatArea.razor injections**

```razor
@using SmallEBot.Application.Conversation
@using SmallEBot.Application.Streaming
@using SmallEBot.Application.Agents
@using SmallEBot.Application.User
@using SmallEBot.Application.Workspace

@inject IAgentConversationService ConversationPipeline
@inject IAgentRunner AgentRunner
@inject IWorkspaceUploadService UploadService
@inject IContextUsageEstimator ContextUsageEstimator
@inject IAgentConfigService AgentConfigService
@inject ISkillsConfigService SkillsConfigService
@inject IUserNameProvider UserNameProvider
@inject ChatPresentationService Presentation
```

**Step 2: Remove any direct Host service dependencies**

Replace any usage of concrete Host types with Contracts interfaces.

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Run application**

Run: `dotnet run --project SmallEBot`
Expected: Application starts without errors

**Step 5: Final commit**

```bash
git add -A
git commit -m "refactor(ui): use Application.Contracts interfaces in Blazor components

- Complete Phase 3 DDD restructuring
- All Blazor components now depend on Contracts abstractions
- Clear separation between interface definitions and implementations"
```

---

## Phase 3 Summary

After Phase 3 completion:

```
SmallEBot.Domain/
├── Common/Services/
│   └── ITokenizer.cs
├── Conversations/Services/
│   └── IContextWindowEstimator.cs
└── (no external dependencies)

SmallEBot.Application.Contracts/  ← NEW PROJECT
├── Conversation/
│   ├── ICompressionService.cs
│   ├── IContextUsageEstimator.cs
│   └── IAgentConversationService.cs
├── Session/
│   ├── ISessionFileService.cs
│   ├── ISessionManager.cs
│   └── IAgentSessionReader.cs
├── Streaming/
│   ├── IAgentRunner.cs
│   ├── IStreamSink.cs
│   └── StreamUpdate.cs
├── Context/
│   └── IContextWindowManager.cs
├── Agents/
│   ├── IAgentConfigService.cs
│   ├── ISkillsConfigService.cs
│   └── IModelConfigService.cs
├── Workspace/
│   └── IWorkspaceUploadService.cs
└── User/
    └── IUserNameProvider.cs

SmallEBot.Application/
├── Conversation/
│   └── AgentConversationService.cs  (implementation only)
├── Context/
│   └── ContextWindowManager.cs  (implementation only)
└── (references Contracts + Domain)

SmallEBot.Infrastructure/
├── Services/
│   ├── CompressionService.cs
│   ├── CharEstimateTokenizer.cs
│   └── DeepSeekTokenizer.cs
├── Persistence/
│   └── (existing repositories)
└── (references Domain only)
```

**Dependency Flow:**
```
Blazor UI (Host)
    ↓
Application.Contracts (interfaces only)
    ↓
Application (implementations)
    ↓
Infrastructure (repositories + services)
    ↓
Domain (entities + value objects)
```

---

**Phase 3 Complete!**
