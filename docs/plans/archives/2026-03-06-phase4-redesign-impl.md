# Phase 4 Redesign: Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Design Doc:** `docs/plans/2026-03-06-phase4-redesign-design.md`

**Goal:** Migrate from SQLite database to file-based storage with minimal metadata. Only store user attachments in turns, read all message content from AgentSession.

**Key Insight:** AgentSession already contains all message content (text, reasoning, tool calls) with timestamps. We only need to store user attachment metadata.

---

## Phase 4.1: Add Turn Metadata Model (Non-Breaking)

### Task 4.1.1: Create TurnMetadata Class

**Files:**
- Create: `SmallEBot.Core/Models/TurnMetadata.cs`

**Step 1: Create the model class**

```csharp
// SmallEBot.Core/Models/TurnMetadata.cs
namespace SmallEBot.Core.Models;

/// <summary>
/// Minimal turn metadata stored alongside AgentSession.
/// Only contains data not available in AgentSession (attachments, skills).
/// </summary>
public class TurnMetadata
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> AttachedPaths { get; set; } = [];
    public List<string> RequestedSkillIds { get; set; } = [];
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

**Expected:** Build succeeds with 0 errors.

---

### Task 4.1.2: Extend ConversationMetadata with Turns

**Files:**
- Modify: `SmallEBot.Core/Models/ConversationMetadata.cs`

**Step 1: Add Turns property**

```csharp
// SmallEBot.Core/Models/ConversationMetadata.cs
using System.Text.Json;

namespace SmallEBot.Core.Models;

/// <summary>
/// File-based conversation metadata with optional AgentSession data.
/// Stored as JSON in .agents/sessions/{id}.json
/// </summary>
public class ConversationMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Compressed summary of messages before CompressedAt timestamp.
    /// </summary>
    public string? CompressedContext { get; set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// </summary>
    public DateTime? CompressedAt { get; set; }

    /// <summary>
    /// Serialized AgentSession state from SerializeSessionAsync.
    /// </summary>
    public JsonElement? SessionData { get; set; }

    /// <summary>
    /// Turn metadata for UI display (attachments, skills).
    /// AgentSession contains the actual message content.
    /// </summary>
    public List<TurnMetadata> Turns { get; set; } = [];
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

**Expected:** Build succeeds with 0 errors.

---

## Phase 4.2: Migrate AgentConversationService

### Task 4.2.1: Update CreateTurnAndUserMessageAsync

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Current behavior:**
- Inserts turn into database via `repository.AddTurnAndUserMessageAsync()`

**New behavior:**
- Add turn metadata to ConversationMetadata
- Save metadata file
- Still return turn ID for compatibility

**Step 1: Read current implementation**

The current `CreateTurnAndUserMessageAsync` at lines 85-97 calls:
```csharp
return await repository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, attachedPaths, requestedSkillIds, cancellationToken);
```

**Step 2: Replace with file-based implementation**

```csharp
public async Task<Guid> CreateTurnAndUserMessageAsync(
    Guid conversationId,
    string userName,
    string userMessage,
    bool useThinking,
    CancellationToken cancellationToken = default,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null)
{
    // Load metadata
    var metadata = await sessionFileService.LoadAsync(conversationId, cancellationToken);
    if (metadata == null)
        throw new InvalidOperationException($"Conversation {conversationId} not found");

    // Generate title for first turn
    var isFirstTurn = metadata.Turns.Count == 0;
    if (isFirstTurn)
    {
        metadata.Title = await agentRunner.GenerateTitleAsync(userMessage, cancellationToken);
    }

    // Create turn metadata
    var turnId = Guid.NewGuid();
    var turn = new TurnMetadata
    {
        Id = turnId,
        CreatedAt = DateTime.UtcNow,
        AttachedPaths = attachedPaths?.ToList() ?? [],
        RequestedSkillIds = requestedSkillIds?.ToList() ?? []
    };
    metadata.Turns.Add(turn);

    // Save metadata
    await sessionFileService.SaveAsync(metadata, cancellationToken);

    return turnId;
}
```

**Step 3: Update GetMessageCountAsync**

```csharp
public Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default)
{
    // Count turns (each turn = 1 user message)
    var metadata = sessionFileService.LoadAsync(conversationId, cancellationToken).Result;
    return Task.FromResult(metadata?.Turns.Count ?? 0);
}
```

**Step 4: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.2.2: Remove Repository Dependency from Turn Operations

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Methods to update:**

1. **StreamResponseAndCompleteAsync** - Remove `repository.CompleteTurnWithAssistantAsync` call
   - The assistant response is already stored in AgentSession via `sessionManager.PersistSessionAsync`

2. **ReplaceUserMessageAsync** - Update to work with turn metadata
3. **PrepareTurnForRegenerateAsync** - Update to read from turn metadata
4. **ReplaceMessageAndRegenerateAsync** - Update accordingly
5. **RegenerateAsync** - Update accordingly

**Key changes:**
- Turn data is stored in ConversationMetadata.Turns
- Message content is stored in AgentSession (via SessionData)
- No database writes for messages

**Step 1: Simplify StreamResponseAndCompleteAsync**

Remove the `repository.CompleteTurnWithAssistantAsync` call - assistant response is already persisted in AgentSession.

```csharp
public async Task StreamResponseAndCompleteAsync(
    Guid conversationId,
    Guid turnId,
    string userMessage,
    bool useThinking,
    IStreamSink sink,
    CancellationToken cancellationToken = default,
    string? commandConfirmationContextId = null,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null)
{
    commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
    conversationTaskContext.SetConversationId(conversationId);
    try
    {
        var updates = new List<StreamUpdate>();
        await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds))
        {
            updates.Add(update);
            await sink.OnNextAsync(update, cancellationToken);
        }
        // No database write needed - AgentSession is persisted by AgentRunnerAdapter
    }
    finally
    {
        conversationTaskContext.SetConversationId(null);
    }
}
```

**Step 2: Update PrepareTurnForRegenerateAsync**

```csharp
public async Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> PrepareTurnForRegenerateAsync(
    Guid conversationId,
    string userName,
    Guid turnId,
    CancellationToken cancellationToken = default)
{
    var metadata = await sessionFileService.LoadAsync(conversationId, cancellationToken);
    if (metadata == null || metadata.UserName != userName) return null;

    var turn = metadata.Turns.FirstOrDefault(t => t.Id == turnId);
    if (turn == null) return null;

    // Get user message from AgentSession (need to deserialize)
    // For now, return empty string - will be replaced in next task
    return (turn.Id, "", false, turn.AttachedPaths, turn.RequestedSkillIds);
}
```

**Note:** Reading user message from AgentSession requires Task 4.3 (AgentSessionReader).

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 4.3: Create AgentSessionReader Service

### Task 4.3.1: Create IAgentSessionReader Interface

**Files:**
- Create: `SmallEBot.Application/Session/IAgentSessionReader.cs`

```csharp
// SmallEBot.Application/Session/IAgentSessionReader.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Session;

/// <summary>
/// Reads message history from serialized AgentSession data.
/// </summary>
public interface IAgentSessionReader
{
    /// <summary>
    /// Get all chat messages from a conversation's AgentSession.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get user message content for a specific turn.
    /// Turn index = position in turns array.
    /// User message index = turnIndex * 2.
    /// </summary>
    Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int turnIndex,
        CancellationToken ct = default);
}
```

**Step 1: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.3.2: Implement AgentSessionReader

**Files:**
- Create: `SmallEBot/Services/Session/AgentSessionReader.cs`

```csharp
// SmallEBot/Services/Session/AgentSessionReader.cs
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Session;

namespace SmallEBot.Services.Session;

/// <summary>
/// Reads messages from serialized AgentSession without requiring AIAgent instance.
/// Directly parses the JSON structure for efficiency.
/// </summary>
public sealed class AgentSessionReader(IAgentBuilder agentBuilder, ISessionFileService fileService) : IAgentSessionReader
{
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var metadata = await fileService.LoadAsync(conversationId, ct);
        if (metadata?.SessionData == null) return [];

        try
        {
            // Extract messages from SessionData JSON
            // Structure: { "stateBag": { "InMemoryChatHistoryProvider": { "messages": [...] } } }
            var sessionData = metadata.SessionData.Value;
            if (sessionData.TryGetProperty("stateBag"u8, out var stateBag) &&
                stateBag.TryGetProperty("InMemoryChatHistoryProvider"u8, out var historyProvider) &&
                historyProvider.TryGetProperty("messages"u8, out var messages))
            {
                return ParseMessages(messages);
            }
        }
        catch
        {
            // Return empty if parsing fails
        }

        return [];
    }

    public async Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int turnIndex,
        CancellationToken ct = default)
    {
        var messages = await GetMessagesAsync(conversationId, ct);
        var userIndex = turnIndex * 2;
        if (userIndex >= messages.Count) return null;

        var userMessage = messages[userIndex];
        return userMessage.Text;
    }

    private static List<ChatMessage> ParseMessages(JsonElement messagesArray)
    {
        var messages = new List<ChatMessage>();

        foreach (var msgElement in messagesArray.EnumerateArray())
        {
            var role = msgElement.TryGetProperty("role"u8, out var roleProp)
                ? roleProp.GetString() ?? "user"
                : "user";

            var chatRole = role.ToLowerInvariant() switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User
            };

            var contents = new List<AIContent>();

            if (msgElement.TryGetProperty("contents"u8, out var contentsArray))
            {
                foreach (var content in contentsArray.EnumerateArray())
                {
                    var type = content.TryGetProperty("$type"u8, out var typeProp)
                        ? typeProp.GetString() ?? "text"
                        : "text";

                    var text = content.TryGetProperty("text"u8, out var textProp)
                        ? textProp.GetString() ?? ""
                        : "";

                    contents.Add(type.ToLowerInvariant() switch
                    {
                        "reasoning" => new TextReasoningContent(text),
                        "function_call" => ParseFunctionCall(content),
                        "function_result" => ParseFunctionResult(content),
                        _ => new TextContent(text)
                    });
                }
            }

            messages.Add(new ChatMessage(chatRole, contents));
        }

        return messages;
    }

    private static FunctionCallContent ParseFunctionCall(JsonElement element)
    {
        var name = element.TryGetProperty("name"u8, out var nameProp) ? nameProp.GetString() ?? "" : "";
        var callId = element.TryGetProperty("callId"u8, out var idProp) ? idProp.GetString() ?? "" : "";
        var arguments = element.TryGetProperty("arguments"u8, out var argsProp)
            ? argsProp.Deserialize<Dictionary<string, object?>>()
            : new Dictionary<string, object?>();

        return new FunctionCallContent(name, callId, arguments ?? new Dictionary<string, object?>());
    }

    private static FunctionResultContent ParseFunctionResult(JsonElement element)
    {
        var callId = element.TryGetProperty("callId"u8, out var idProp) ? idProp.GetString() ?? "" : "";
        var result = element.TryGetProperty("result"u8, out var resultProp) ? resultProp.GetString() ?? "" : "";

        return new FunctionResultContent(callId, result);
    }
}
```

**Step 2: Register in DI**

```csharp
// In ServiceCollectionExtensions.cs
services.AddScoped<IAgentSessionReader, AgentSessionReader>();
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 4.4: Update Compression Service

### Task 4.4.1: Update ICompressionService Interface

**Files:**
- Modify: `SmallEBot.Application/Conversation/ICompressionService.cs`

**Step 1: Change to use AI types**

```csharp
// SmallEBot.Application/Conversation/ICompressionService.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Conversation;

/// <summary>Service for compressing conversation history using LLM.</summary>
public interface ICompressionService
{
    /// <summary>Generate a compressed summary of conversation history.</summary>
    /// <param name="messages">Chat messages to compress (from AgentSession).</param>
    /// <param name="toolResultMaxLength">Maximum length for truncated tool results.</param>
    /// <param name="existingSummary">Existing compressed summary to merge with new content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Compressed summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default);
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.4.2: Update CompressionService Implementation

**Files:**
- Modify: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 1: Update to use Microsoft.Extensions.AI.ChatMessage**

```csharp
// SmallEBot/Services/Agent/CompressionService.cs
using System.Text;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;

namespace SmallEBot.Services.Agent;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService(IAgentBuilder agentBuilder, ILogger<CompressionService> logger) : ICompressionService
{
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

            foreach (var msg in messages)
            {
                var role = msg.Role == ChatRole.User ? "User" : "Assistant";
                sb.AppendLine($"[{role}]: {msg.Text}");
                sb.AppendLine();

                // Include tool calls and results
                foreach (var content in msg.Contents)
                {
                    if (content is FunctionCallContent fnCall)
                    {
                        sb.AppendLine($"[Tool Call: {fnCall.Name}]");
                        sb.AppendLine($"Arguments: {ToJsonString(fnCall.Arguments)}");
                        sb.AppendLine();
                    }
                    else if (content is FunctionResultContent fnResult)
                    {
                        var result = TruncateResult(fnResult.Result?.ToString(), toolResultMaxLength);
                        sb.AppendLine($"[Tool Result]: {result}");
                        sb.AppendLine();
                    }
                    else if (content is TextReasoningContent reasoning)
                    {
                        sb.AppendLine($"[Thinking]: {reasoning.Text[..Math.Min(200, reasoning.Text.Length)]}...");
                        sb.AppendLine();
                    }
                }
            }
        }

        try
        {
            var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, ct);
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, CompactPrompt),
                new(ChatRole.User, sb.ToString())
            };

            var chatOptions = new ChatOptions { Reasoning = null };
            var runOptions = new ChatClientAgentRunOptions(chatOptions);
            var result = await agent.RunAsync(chatMessages, null, runOptions, ct);
            logger.LogInformation("Compression generated summary: {Length} chars", result.Text.Length);
            return result.Text;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate compression summary");
            return null;
        }
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    private static string? ToJsonString(object? value)
    {
        if (value == null) return null;
        return System.Text.Json.JsonSerializer.Serialize(value);
    }
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.4.3: Update AgentConversationService Compression

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Update CompactConversationAsync**

```csharp
public async Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default)
{
    if (_compressingConversations.Contains(conversationId))
        return false;

    _compressingConversations.Add(conversationId);
    CompressionStarted?.Invoke(conversationId);

    try
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null)
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }

        // Get messages from AgentSession
        var messages = await sessionReader.GetMessagesAsync(conversationId, ct);
        if (messages.Count == 0)
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }

        // Generate summary
        var summary = await compressionService.GenerateSummaryAsync(
            messages,
            toolResultMaxProvider.GetToolResultMaxLength(),
            metadata.CompressedContext,
            ct);

        if (string.IsNullOrWhiteSpace(summary))
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }

        // Update metadata
        metadata.CompressedContext = summary;
        metadata.CompressedAt = DateTime.UtcNow;
        await sessionFileService.SaveAsync(metadata, ct);

        CompressionCompleted?.Invoke(conversationId, true);
        return true;
    }
    catch
    {
        CompressionCompleted?.Invoke(conversationId, false);
        return false;
    }
    finally
    {
        _compressingConversations.Remove(conversationId);
    }
}
```

**Step 2: Add IAgentSessionReader dependency**

```csharp
public sealed class AgentConversationService(
    IConversationRepository repository,  // Will be removed in Phase 4.5
    ISessionFileService sessionFileService,
    ISessionManager sessionManager,
    IAgentSessionReader sessionReader,   // NEW
    IAgentRunner agentRunner,
    // ... rest unchanged
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 4.5: Remove Database Layer

### Task 4.5.1: Remove Entity Files

**Files to delete:**
- `SmallEBot.Core/Entities/ChatMessage.cs`
- `SmallEBot.Core/Entities/ToolCall.cs`
- `SmallEBot.Core/Entities/ThinkBlock.cs`
- `SmallEBot.Core/Entities/ConversationTurn.cs`

**Step 1: Delete files**

```bash
rm SmallEBot.Core/Entities/ChatMessage.cs
rm SmallEBot.Core/Entities/ToolCall.cs
rm SmallEBot.Core/Entities/ThinkBlock.cs
rm SmallEBot.Core/Entities/ConversationTurn.cs
```

**Step 2: Build to find remaining references**

```bash
dotnet build SmallEBot
```

**Step 3: Fix any remaining references**

Check for usages and remove or update:
- `SmallEBot.Core/Models/TimelineItem.cs` - Delete or simplify
- `SmallEBot.Core/Models/ChatBubble.cs` - Update to use AI types
- `SmallEBot.Core/ConversationBubbleHelper.cs` - Update or remove

---

### Task 4.5.2: Remove Repository Files

**Files to delete:**
- `SmallEBot.Core/Repositories/IConversationRepository.cs`
- `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`

**Step 1: Delete files**

```bash
rm SmallEBot.Core/Repositories/IConversationRepository.cs
rm SmallEBot.Infrastructure/Repositories/ConversationRepository.cs
```

**Step 2: Remove repository dependency from AgentConversationService**

```csharp
// Remove from constructor:
// IConversationRepository repository

// Remove field:
// private readonly IConversationRepository _repository;
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.5.3: Clean Up DbContext

**Files:**
- Modify: `SmallEBot.Infrastructure/Data/SmallEBotDbContext.cs`

**Step 1: Remove entity DbSets**

Remove these if present:
```csharp
public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
public DbSet<ToolCall> ToolCalls => Set<ToolCall>();
public DbSet<ThinkBlock> ThinkBlocks => Set<ThinkBlock>();
public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
```

Keep:
```csharp
public DbSet<Conversation> Conversations => Set<Conversation>();
```

**Note:** If Conversation entity is no longer needed, the entire database layer can be removed.

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.5.4: Update DI Registrations

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Remove repository registration**

Remove:
```csharp
services.AddScoped<IConversationRepository, ConversationRepository>();
```

**Step 2: Add new services**

Ensure these are registered:
```csharp
services.AddScoped<IAgentSessionReader, AgentSessionReader>();
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 4.6: Final Validation

### Task 4.6.1: Comprehensive Build

**Step 1: Clean and rebuild**

```bash
dotnet clean
dotnet build
```

**Expected:** Build succeeds with 0 errors, 0 warnings.

---

### Task 4.6.2: Manual Integration Test

**Step 1: Run application**

```bash
dotnet run --project SmallEBot
```

**Step 2: Verify features**

- [ ] Create new conversation
- [ ] Send message and receive response
- [ ] Verify session file created in `.agents/sessions/`
- [ ] Check that turns array is populated
- [ ] List conversations in sidebar
- [ ] Delete conversation
- [ ] Context compression works
- [ ] Regenerate response works

---

### Task 4.6.3: Commit Changes

**Step 1: Stage all changes**

```bash
git add -A
```

**Step 2: Create commit**

```bash
git commit -m "$(cat <<'EOF'
refactor: migrate to minimal metadata storage with AgentSession

Breaking changes:
- Remove SQLite database for message storage
- Store only turn metadata (attachments, skills) in ConversationMetadata
- Read all message content from AgentSession
- Remove ChatMessage, ToolCall, ThinkBlock, ConversationTurn entities
- Remove IConversationRepository and ConversationRepository

New services:
- IAgentSessionReader: Reads messages from serialized AgentSession
- TurnMetadata: Minimal per-turn metadata (id, createdAt, attachments, skills)

Updated services:
- AgentConversationService: Writes to turn metadata, no database writes
- CompressionService: Uses AI ChatMessage types
- ICompressionService: Updated interface

Benefits:
- Single source of truth: AgentSession for message content
- Minimal file size: Only store what AgentSession lacks
- Simpler architecture: No database synchronization needed

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Summary

| Phase | Tasks | Description |
|-------|-------|-------------|
| 4.1 | 2 | Add TurnMetadata model |
| 4.2 | 2 | Migrate AgentConversationService |
| 4.3 | 3 | Create AgentSessionReader, update compression |
| 4.4 | 3 | Update compression service |
| 4.5 | 4 | Remove database layer |
| 4.6 | 3 | Final validation |

**Total: 17 tasks**

**Estimated effort:** 2-3 hours
