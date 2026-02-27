# Automatic Context Compression Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement automatic context compression when usage reaches 80%, with manual trigger support.

**Architecture:** Add compressed context fields to database, create compact skill, inject summary into system prompt, filter messages for LLM history.

**Tech Stack:** C# 14, .NET 10, EF Core, System.Text.Json

---

### Task 1: Add Compression Fields to Conversation Entity

**Files:**
- Modify: `SmallEBot.Core/Entities/Conversation.cs`

**Step 1: Add properties to Conversation.cs**

Add after `UpdatedAt` property (line 13):

```csharp
public DateTime UpdatedAt { get; set; }

/// <summary>Compressed summary of messages before CompressedAt. Injected into system prompt for LLM context.</summary>
public string? CompressedContext { get; set; }

/// <summary>Timestamp when compression occurred. Messages before this are excluded from LLM history but visible in UI.</summary>
public DateTime? CompressedAt { get; set; }

public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot.Core/Entities/Conversation.cs
git commit -m "feat(core): add CompressedContext and CompressedAt to Conversation entity"
```

---

### Task 2: Create EF Migration for Compression Fields

**Files:**
- Generated: `SmallEBot.Infrastructure/Data/Migrations/`

**Step 1: Generate migration**

Run: `dotnet ef migrations add AddCompressionFields --project SmallEBot.Infrastructure --startup-project SmallEBot`
Expected: Migration file created

**Step 2: Verify migration content**

Read the generated migration file. Should contain:
- AddColumn `CompressedContext` (TEXT, nullable)
- AddColumn `CompressedAt` (TEXT, nullable)

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Data/Migrations/
git commit -m "feat(db): add migration for CompressedContext and CompressedAt columns"
```

---

### Task 3: Add UpdateCompressionAsync to Repository Interface

**Files:**
- Modify: `SmallEBot.Core/Repositories/IConversationRepository.cs`

**Step 1: Add method to interface**

Add after `GetTurnForRegenerateAsync` method:

```csharp
/// <summary>Update compression fields for a conversation. Set compressedAt to null to clear compression.</summary>
Task UpdateCompressionAsync(Guid conversationId, string? compressedContext, DateTime? compressedAt, CancellationToken ct = default);
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build errors (interface not implemented) - expected

**Step 3: Commit**

```bash
git add SmallEBot.Core/Repositories/IConversationRepository.cs
git commit -m "feat(core): add UpdateCompressionAsync to IConversationRepository"
```

---

### Task 4: Implement UpdateCompressionAsync in Repository

**Files:**
- Modify: `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`

**Step 1: Implement the method**

Add at the end of the class (before closing brace):

```csharp
public async Task UpdateCompressionAsync(Guid conversationId, string? compressedContext, DateTime? compressedAt, CancellationToken ct = default)
{
    var conv = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
    if (conv == null) return;

    conv.CompressedContext = compressedContext;
    conv.CompressedAt = compressedAt;
    conv.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync(ct);
}
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Repositories/ConversationRepository.cs
git commit -m "feat(db): implement UpdateCompressionAsync in ConversationRepository"
```

---

### Task 5: Add CompressionThreshold to IAgentConfigService

**Files:**
- Modify: `SmallEBot/Services/Agent/IAgentConfigService.cs`

**Step 1: Add method to interface**

Add after `GetToolResultMaxLength`:

```csharp
/// <summary>Context usage ratio threshold (0.0-1.0) that triggers automatic compression. Default: 0.8 (80%).</summary>
Task<double> GetCompressionThresholdAsync(CancellationToken ct = default);

/// <summary>Synchronous version for convenience.</summary>
double GetCompressionThreshold();
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build errors (interface not implemented) - expected

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/IAgentConfigService.cs
git commit -m "feat(agent): add CompressionThreshold to IAgentConfigService"
```

---

### Task 6: Implement CompressionThreshold in AgentConfigService

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentConfigService.cs`

**Step 1: Add constants**

Add after `MaxToolResultMaxLength` constant:

```csharp
private const double DefaultCompressionThreshold = 0.8;
private const double MinCompressionThreshold = 0.5;
private const double MaxCompressionThreshold = 0.95;
```

**Step 2: Add implementation methods**

Add after `GetToolResultMaxLengthAsync`:

```csharp
public double GetCompressionThreshold()
{
    if (!File.Exists(_filePath))
        return DefaultCompressionThreshold;
    try
    {
        var json = File.ReadAllText(_filePath);
        var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
        var raw = data?.CompressionThreshold ?? 0;
        return raw >= MinCompressionThreshold
            ? Math.Clamp(raw, MinCompressionThreshold, MaxCompressionThreshold)
            : DefaultCompressionThreshold;
    }
    catch
    {
        return DefaultCompressionThreshold;
    }
}

public async Task<double> GetCompressionThresholdAsync(CancellationToken ct = default)
{
    if (!File.Exists(_filePath))
        return DefaultCompressionThreshold;
    try
    {
        var json = await File.ReadAllTextAsync(_filePath, ct);
        var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
        var raw = data?.CompressionThreshold ?? 0;
        return raw >= MinCompressionThreshold
            ? Math.Clamp(raw, MinCompressionThreshold, MaxCompressionThreshold)
            : DefaultCompressionThreshold;
    }
    catch
    {
        return DefaultCompressionThreshold;
    }
}
```

**Step 3: Update AgentConfigFile class**

Change the inner class to:

```csharp
private sealed class AgentConfigFile
{
    public int ToolResultMaxLength { get; set; } = DefaultToolResultMaxLength;
    public double CompressionThreshold { get; set; } = DefaultCompressionThreshold;
}
```

**Step 4: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/AgentConfigService.cs
git commit -m "feat(agent): implement CompressionThreshold in AgentConfigService"
```

---

### Task 7: Create Compact Skill File

**Files:**
- Create: `SmallEBot/.agents/sys.skills/compact/SKILL.md`

**Step 1: Create skill directory and file**

```bash
mkdir -p SmallEBot/.agents/sys.skills/compact
```

**Step 2: Write SKILL.md content**

```yaml
---
name: compact
description: Compress conversation history into a concise summary. Use when context is running low or user requests /compact.
---

# Context Compression

You are compressing conversation history to save context space.

## Input
You will receive conversation messages (user + assistant + tool calls).

## Task
Generate a structured summary preserving:

1. **Key Decisions**: Important choices made and why
2. **Files Modified**: Paths and what changed (briefly)
3. **Current State**: What's been accomplished, what's pending
4. **Important Context**: Names, values, configurations that matter

## Format
Use this compact format:

```
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
```

Keep total output under 500 tokens. Focus on what's needed to continue the work.
```

**Step 3: Verify file exists**

Run: `cat SmallEBot/.agents/sys.skills/compact/SKILL.md`
Expected: File content displayed

**Step 4: Commit**

```bash
git add SmallEBot/.agents/sys.skills/compact/SKILL.md
git commit -m "feat(skills): add compact skill for context compression"
```

---

### Task 8: Add CompactContext Tool Name Constant

**Files:**
- Modify: `SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs`

**Step 1: Add constant**

Add after `GenerateSkill` constant:

```csharp
// Conversation analysis (ConversationToolProvider)
public const string ReadConversationData = nameof(ReadConversationData);

// Context compression (CompressionToolProvider)
public const string CompactContext = nameof(CompactContext);
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs
git commit -m "feat(tools): add CompactContext to BuiltInToolNames"
```

---

### Task 9: Create CompressionToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/CompressionToolProvider.cs`

**Step 1: Create the provider file**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides context compression tool. Not exposed to LLM - called internally by AgentRunnerAdapter when context exceeds threshold.</summary>
public sealed class CompressionToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository,
    IAgentConfigService agentConfig,
    ICompressionService compressionService) : IToolProvider
{
    public string Name => "Compression";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        // This tool is NOT exposed to the LLM - it's called internally
        // Return empty to hide from LLM
        yield break;
    }

    /// <summary>Compress conversation history and store summary. Called by AgentRunnerAdapter when context exceeds threshold.</summary>
    public async Task<CompressionResult> CompactContextAsync(CancellationToken ct = default)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return new CompressionResult(false, "No active conversation");

        // Get conversation to check existing compression
        var conversation = await repository.GetByIdAsync(conversationId.Value, "", ct);
        if (conversation == null)
            return new CompressionResult(false, "Conversation not found");

        // Get messages to compress (before existing CompressedAt, or all if first compression)
        var allMessages = await repository.GetMessagesForConversationAsync(conversationId.Value, ct);
        var messagesToCompress = conversation.CompressedAt == null
            ? allMessages
            : allMessages.Where(m => m.CreatedAt <= conversation.CompressedAt.Value).ToList();

        if (messagesToCompress.Count == 0)
            return new CompressionResult(false, "No messages to compress");

        // Get tool calls for context
        var toolCalls = await repository.GetToolCallsForConversationAsync(conversationId.Value, ct);
        var toolCallsToCompress = conversation.CompressedAt == null
            ? toolCalls
            : toolCalls.Where(t => t.CreatedAt <= conversation.CompressedAt.Value).ToList();

        // Call LLM to generate summary
        var summary = await compressionService.GenerateSummaryAsync(
            messagesToCompress,
            toolCallsToCompress,
            agentConfig.GetToolResultMaxLength(),
            ct);

        if (string.IsNullOrWhiteSpace(summary))
            return new CompressionResult(false, "Failed to generate summary");

        // Store compressed context
        var newCompressedAt = DateTime.UtcNow;
        await repository.UpdateCompressionAsync(conversationId.Value, summary, newCompressedAt, ct);

        return new CompressionResult(true, $"Compressed {messagesToCompress.Count} messages", summary);
    }
}

public record CompressionResult(bool Success, string Message, string? Summary = null);
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build errors - missing ICompressionService

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/CompressionToolProvider.cs
git commit -m "feat(tools): add CompressionToolProvider for context compression"
```

---

### Task 10: Create ICompressionService and Implementation

**Files:**
- Create: `SmallEBot/Services/Agent/ICompressionService.cs`
- Create: `SmallEBot/Services/Agent/CompressionService.cs`

**Step 1: Create interface**

`SmallEBot/Services/Agent/ICompressionService.cs`:

```csharp
using SmallEBot.Core.Entities;

namespace SmallEBot.Services.Agent;

/// <summary>Service for compressing conversation history using LLM.</summary>
public interface ICompressionService
{
    /// <summary>Generate a compressed summary of conversation history.</summary>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default);
}
```

**Step 2: Create implementation**

`SmallEBot/Services/Agent/CompressionService.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Core.Entities;

namespace SmallEBot.Services.Agent;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService : ICompressionService
{
    private readonly IChatClient _chatClient;

    private const string CompactPrompt = """
You are compressing conversation history to save context space.

## Input
You will receive conversation messages (user + assistant + tool calls).

## Task
Generate a structured summary preserving:

1. **Key Decisions**: Important choices made and why
2. **Files Modified**: Paths and what changed (briefly)
3. **Current State**: What's been accomplished, what's pending
4. **Important Context**: Names, values, configurations that matter

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

Keep total output under 500 tokens. Focus on what's needed to continue the work.
""";

    public CompressionService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolCall> toolCalls,
        int toolResultMaxLength,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return null;

        // Build conversation text for compression
        var sb = new StringBuilder();
        sb.AppendLine("## Conversation to Compress");
        sb.AppendLine();

        foreach (var msg in messages.Where(m => m.ReplacedByMessageId == null))
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"[{role}]: {msg.Content}");
            sb.AppendLine();
        }

        // Add tool calls with truncated results
        foreach (var tc in toolCalls)
        {
            var result = TruncateResult(tc.Result, toolResultMaxLength);
            sb.AppendLine($"[Tool: {tc.ToolName}] -> {result}");
            sb.AppendLine();
        }

        var messages_for_llm = new List<ChatMessage>
        {
            new(ChatRole.System, CompactPrompt),
            new(ChatRole.User, sb.ToString())
        };

        try
        {
            var response = await _chatClient.CompleteAsync(messages_for_llm, cancellationToken: ct);
            return response.Message.Text;
        }
        catch
        {
            return null;
        }
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }
}
```

**Step 3: Register in DI**

Modify `SmallEBot/Extensions/ServiceCollectionExtensions.cs`:

Add after `IAgentConfigService` registration:

```csharp
services.AddSingleton<IAgentConfigService, AgentConfigService>();
services.AddScoped<ICompressionService, CompressionService>();
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build errors - IChatClient not registered, fix in next task

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/ICompressionService.cs
git add SmallEBot/Services/Agent/CompressionService.cs
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(agent): add ICompressionService for LLM-based context compression"
```

---

### Task 11: Update AgentContextFactory to Inject Compressed Context

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`

**Step 1: Add IConversationRepository dependency**

Change the constructor from:

```csharp
public sealed class AgentContextFactory(ISkillsConfigService skillsConfig, ITerminalConfigService terminalConfig) : IAgentContextFactory
```

To:

```csharp
public sealed class AgentContextFactory(
    ISkillsConfigService skillsConfig,
    ITerminalConfigService terminalConfig,
    ICurrentConversationService currentConversation,
    IConversationRepository conversationRepository) : IAgentContextFactory
```

**Step 2: Add using statement**

Add at top:

```csharp
using SmallEBot.Services.Conversation;
```

**Step 3: Modify BuildSystemPromptAsync to include compressed context**

Change the method to:

```csharp
public async Task<string> BuildSystemPromptAsync(CancellationToken ct = default)
{
    var skills = await skillsConfig.GetMetadataForAgentAsync(ct);
    var blacklist = await terminalConfig.GetCommandBlacklistAsync(ct);

    var sections = new List<string> { BuildBaseInstructions() };

    // Add compressed context if available
    var compressedContext = await GetCompressedContextAsync(ct);
    if (!string.IsNullOrEmpty(compressedContext))
    {
        sections.Add($"# Conversation Summary\n\n{compressedContext}");
    }

    var skillsBlock = BuildSkillsBlock(skills);
    if (!string.IsNullOrEmpty(skillsBlock)) sections.Add(skillsBlock);

    var blacklistBlock = BuildTerminalBlacklistBlock(blacklist);
    if (!string.IsNullOrEmpty(blacklistBlock)) sections.Add(blacklistBlock);

    _cachedSystemPrompt = string.Join("\n\n", sections);
    return _cachedSystemPrompt;
}

private async Task<string?> GetCompressedContextAsync(CancellationToken ct)
{
    var conversationId = currentConversation.CurrentConversationId;
    if (conversationId == null) return null;

    var conversation = await conversationRepository.GetByIdAsync(conversationId.Value, "", ct);
    return conversation?.CompressedContext;
}
```

**Step 4: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/AgentContextFactory.cs
git commit -m "feat(agent): inject compressed context into system prompt"
```

---

### Task 12: Update AgentCacheService Token Estimation

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentCacheService.cs`

**Step 1: Add IConversationRepository dependency**

Change constructor from:

```csharp
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer,
    IAgentConfigService agentConfig) : IAsyncDisposable
```

To:

```csharp
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer,
    IAgentConfigService agentConfig) : IAsyncDisposable
```

(No change needed - already has conversationRepository)

**Step 2: Update GetEstimatedContextUsageDetailAsync**

Change the method to filter by CompressedAt and include compressed context:

```csharp
public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default)
{
    // Get conversation to check CompressedAt
    var conversation = await conversationRepository.GetByIdAsync(conversationId, "", ct);

    var allMessages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
    var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);
    var toolResultMaxLength = agentConfig.GetToolResultMaxLength();

    // Filter messages by CompressedAt
    var filteredMessages = conversation?.CompressedAt != null
        ? allMessages.Where(m => m.CreatedAt > conversation.CompressedAt.Value).ToList()
        : allMessages;

    var filteredToolCalls = conversation?.CompressedAt != null
        ? toolCalls.Where(t => t.CreatedAt > conversation.CompressedAt.Value).ToList()
        : toolCalls;

    // Truncate tool results to match what's actually sent to LLM
    var truncatedToolCalls = filteredToolCalls.Select(t => new ToolCallWithTruncatedResult
    {
        ToolName = t.ToolName ?? "",
        Arguments = t.Arguments ?? "",
        Result = TruncateToolResult(t.Result, toolResultMaxLength) ?? ""
    }).ToList();

    var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;

    // Include compressed context in token count
    var compressedContextTokens = 0;
    if (!string.IsNullOrEmpty(conversation?.CompressedContext))
    {
        compressedContextTokens = tokenizer.CountTokens(conversation.CompressedContext);
    }

    var json = SerializeRequestJsonForTokenCount(systemPrompt, filteredMessages, truncatedToolCalls, []);
    var rawTokens = tokenizer.CountTokens(json);
    var usedTokens = (int)Math.Ceiling(rawTokens * 1.05) + compressedContextTokens;
    var contextWindow = agentBuilder.GetContextWindowTokens();

    if (contextWindow <= 0) return new ContextUsageEstimate(0, usedTokens, contextWindow);
    var ratio = Math.Min(1.0, usedTokens / (double)contextWindow);
    return new ContextUsageEstimate(Math.Round(ratio, 3), usedTokens, contextWindow);
}
```

**Step 3: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 4: Commit**

```bash
git add SmallEBot/Services/Agent/AgentCacheService.cs
git commit -m "fix(agent): include compressed context in token estimation"
```

---

### Task 13: Update AgentRunnerAdapter for Auto-Compression

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add dependencies**

Change constructor from:

```csharp
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager,
    IAgentConfigService agentConfig) : IAgentRunner
```

To:

```csharp
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager,
    IAgentConfigService agentConfig,
    ICompressionService compressionService) : IAgentRunner
```

**Step 2: Add auto-compression check in RunStreamingAsync**

After line 31 (`var agent = await...`), add:

```csharp
var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);

// Auto-compression check
await CheckAndCompressAsync(conversationId, cancellationToken);

var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
```

**Step 3: Add helper method**

Add after `BuildToolSummary` method:

```csharp
private async Task CheckAndCompressAsync(Guid conversationId, CancellationToken ct)
{
    var conversation = await conversationRepository.GetByIdAsync(conversationId, "", ct);
    if (conversation == null) return;

    // Get current context usage
    var allMessages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
    var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);
    var toolResultMaxLength = agentConfig.GetToolResultMaxLength();

    // Filter by CompressedAt
    var filteredMessages = conversation.CompressedAt != null
        ? allMessages.Where(m => m.CreatedAt > conversation.CompressedAt.Value).ToList()
        : allMessages;

    var filteredToolCalls = conversation.CompressedAt != null
        ? toolCalls.Where(t => t.CreatedAt > conversation.CompressedAt.Value).ToList()
        : toolCalls;

    // Estimate tokens
    var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? "";
    var compressedContextTokens = conversation.CompressedContext != null
        ? EstimateTokens(conversation.CompressedContext)
        : 0;

    var messageTokens = filteredMessages.Sum(m => EstimateTokens(m.Content ?? "") + 4);
    var toolCallTokens = filteredToolCalls.Sum(t =>
        EstimateTokens(t.ToolName ?? "") +
        EstimateTokens(t.Arguments ?? "") +
        EstimateTokens(TruncateToolResult(t.Result, toolResultMaxLength)));

    var totalTokens = EstimateTokens(systemPrompt) + compressedContextTokens + messageTokens + toolCallTokens;
    var contextWindow = agentBuilder.GetContextWindowTokens();
    var threshold = agentConfig.GetCompressionThreshold();

    if (contextWindow > 0 && totalTokens / (double)contextWindow >= threshold)
    {
        // Trigger compression
        await CompressConversationAsync(conversationId, allMessages, toolCalls, conversation.CompressedAt, ct);
    }
}

private async Task CompressConversationAsync(
    Guid conversationId,
    List<Core.Entities.ChatMessage> allMessages,
    List<Core.Entities.ToolCall> allToolCalls,
    DateTime? existingCompressedAt,
    CancellationToken ct)
{
    // Get messages to compress (those before existing CompressedAt, or all if first compression)
    var messagesToCompress = existingCompressedAt != null
        ? allMessages.Where(m => m.CreatedAt <= existingCompressedAt.Value).ToList()
        : allMessages;

    var toolCallsToCompress = existingCompressedAt != null
        ? allToolCalls.Where(t => t.CreatedAt <= existingCompressedAt.Value).ToList()
        : allToolCalls;

    if (messagesToCompress.Count == 0) return;

    var toolResultMaxLength = agentConfig.GetToolResultMaxLength();
    var summary = await compressionService.GenerateSummaryAsync(
        messagesToCompress,
        toolCallsToCompress,
        toolResultMaxLength,
        ct);

    if (!string.IsNullOrWhiteSpace(summary))
    {
        await conversationRepository.UpdateCompressionAsync(conversationId, summary, DateTime.UtcNow, ct);
    }
}

private int EstimateTokens(string text) => text.Length / 4; // Rough estimate
```

**Step 4: Filter history by CompressedAt**

After `var history = await...`, filter messages:

```csharp
var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);

// Get conversation to check CompressedAt
var conversation = await conversationRepository.GetByIdAsync(conversationId, "", cancellationToken);

// Filter history by CompressedAt
var filteredHistory = conversation?.CompressedAt != null
    ? history.Where(m => m.CreatedAt > conversation.CompressedAt.Value).ToList()
    : history;

var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, cancellationToken);

// Filter tool calls by CompressedAt
var filteredToolCalls = conversation?.CompressedAt != null
    ? toolCalls.Where(t => t.CreatedAt > conversation.CompressedAt.Value).ToList()
    : toolCalls;
```

**Step 5: Update variable references**

Change `history` to `filteredHistory` and `toolCalls` to `filteredToolCalls` in the rest of the method.

**Step 6: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 7: Commit**

```bash
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): add auto-compression when context exceeds 80%"
```

---

### Task 14: Register CompressionToolProvider in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Register CompressionToolProvider**

Add after other tool providers:

```csharp
services.AddScoped<CompressionToolProvider>();
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register CompressionToolProvider in DI"
```

---

### Task 15: Add Compression State to IAgentConversationService

**Files:**
- Modify: `SmallEBot.Application/Conversation/IAgentConversationService.cs`

**Step 1: Add compression events**

Add to the interface:

```csharp
/// <summary>Fired when compression starts. UI should show compression indicator and disable input.</summary>
event Action<Guid>? CompressionStarted;

/// <summary>Fired when compression completes. UI should hide indicator and re-enable input.</summary>
event Action<Guid, bool>? CompressionCompleted; // conversationId, success

/// <summary>Manually trigger compression for a conversation.</summary>
Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default);
```

**Step 2: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build errors (not implemented) - expected

**Step 3: Commit**

```bash
git add SmallEBot.Application/Conversation/IAgentConversationService.cs
git commit -m "feat(app): add compression events to IAgentConversationService"
```

---

### Task 16: Implement Compression in AgentConversationService

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Add events and fields**

Add to the class:

```csharp
public event Action<Guid>? CompressionStarted;
public event Action<Guid, bool>? CompressionCompleted;

private readonly HashSet<Guid> _compressingConversations = [];
```

**Step 2: Implement CompactConversationAsync**

```csharp
public async Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default)
{
    if (_compressingConversations.Contains(conversationId))
        return false;

    _compressingConversations.Add(conversationId);
    CompressionStarted?.Invoke(conversationId);

    try
    {
        var conversation = await _repository.GetByIdAsync(conversationId, _userName, ct);
        if (conversation == null) return false;

        var allMessages = await _repository.GetMessagesForConversationAsync(conversationId, ct);
        var toolCalls = await _repository.GetToolCallsForConversationAsync(conversationId, ct);

        var messagesToCompress = conversation.CompressedAt != null
            ? allMessages.Where(m => m.CreatedAt <= conversation.CompressedAt.Value).ToList()
            : allMessages;

        var toolCallsToCompress = conversation.CompressedAt != null
            ? toolCalls.Where(t => t.CreatedAt <= conversation.CompressedAt.Value).ToList()
            : toolCalls;

        if (messagesToCompress.Count == 0)
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }

        var summary = await _compressionService.GenerateSummaryAsync(
            messagesToCompress,
            toolCallsToCompress,
            _agentConfig.GetToolResultMaxLength(),
            ct);

        if (string.IsNullOrWhiteSpace(summary))
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }

        await _repository.UpdateCompressionAsync(conversationId, summary, DateTime.UtcNow, ct);
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

**Step 3: Add dependencies to constructor**

Add `ICompressionService compressionService` and `IAgentConfigService agentConfig` to constructor, store as fields.

**Step 4: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 5: Commit**

```bash
git add SmallEBot.Application/Conversation/AgentConversationService.cs
git commit -m "feat(app): implement CompactConversationAsync with events"
```

---

### Task 17: Add Compression Indicator UI

**Files:**
- Modify: `SmallEBot/Components/Chat/StreamingIndicator.razor`

**Step 1: Add compression state parameter**

Add to parameters:

```csharp
[Parameter] public bool IsCompressing { get; set; }
[Parameter] public string? CompressionMessage { get; set; } = "Compressing context...";
```

**Step 2: Update template to show compression indicator**

Change the template:

```razor
@if (IsCompressing)
{
    <div class="d-flex align-center ga-2 pa-3 mud-width-full">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Small" />
        <MudText Typo="Typo.body2" Style="color: var(--mud-palette-text-secondary);">
            @CompressionMessage
        </MudText>
    </div>
}
else if (IsStreaming)
{
    <StreamingMessageView Items="@StreamingItems"
                          FallbackText="@FallbackText"
                          Timestamp="@Timestamp"
                          OnCancel="@OnCancel"
                          ShowWaitingForToolParams="@ShowWaitingForToolParams"
                          WaitingElapsed="@WaitingElapsed"
                          WaitingInReasoning="@WaitingInReasoning"
                          ShowToolCalls="@ShowToolCalls" />
}
```

**Step 3: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/StreamingIndicator.razor
git commit -m "feat(ui): add compression indicator to StreamingIndicator"
```

---

### Task 18: Wire Compression Events in ChatArea

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Add compression state fields**

Add after `_streaming` field:

```csharp
private bool _compressing;
private string _compressionMessage = "";
```

**Step 2: Subscribe to compression events**

In `OnInitialized` or similar setup method:

```csharp
ConversationPipeline.CompressionStarted += OnCompressionStarted;
ConversationPipeline.CompressionCompleted += OnCompressionCompleted;
```

**Step 3: Add event handlers**

```csharp
private void OnCompressionStarted(Guid conversationId)
{
    if (conversationId != ConversationId) return;
    _compressing = true;
    _compressionMessage = "Compressing context...";
    InvokeAsync(StateHasChanged);
}

private void OnCompressionCompleted(Guid conversationId, bool success)
{
    if (conversationId != ConversationId) return;
    _compressing = false;
    _compressionMessage = success ? "" : "Compression failed";
    InvokeAsync(StateHasChanged);
}
```

**Step 4: Unsubscribe in Dispose**

```csharp
ConversationPipeline.CompressionStarted -= OnCompressionStarted;
ConversationPipeline.CompressionCompleted -= OnCompressionCompleted;
```

**Step 5: Pass compression state to StreamingIndicator**

Update the component:

```razor
<StreamingIndicator IsStreaming="@_streaming"
                    IsCompressing="@_compressing"
                    CompressionMessage="@_compressionMessage"
                    ... />
```

**Step 6: Pass compression state to ChatInputArea**

Update the component:

```razor
<ChatInputArea ... IsStreaming="@(_streaming || _compressing)" ... />
```

**Step 7: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 8: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(ui): wire compression events to ChatArea with input blocking"
```

---

### Task 19: Final Verification

**Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded with 0 errors, 0 warnings

**Step 2: Run application to verify startup**

Run: `dotnet run --project SmallEBot`
Expected: No startup errors related to DI

**Step 3: Test compression flow**
1. Start a conversation
2. Let context reach 80% (or trigger manually)
3. Verify: Compression indicator shows
4. Verify: Input is disabled during compression
5. Verify: After compression, input is re-enabled

**Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: resolve any remaining issues"
```

---

## Summary

| Task | Description | Files Changed |
|------|-------------|---------------|
| 1 | Add compression fields to Conversation entity | 1 modified |
| 2 | Create EF migration | +1 new |
| 3 | Add UpdateCompressionAsync to interface | 1 modified |
| 4 | Implement UpdateCompressionAsync | 1 modified |
| 5 | Add CompressionThreshold to interface | 1 modified |
| 6 | Implement CompressionThreshold | 1 modified |
| 7 | Create compact skill file | +1 new |
| 8 | Add CompactContext tool name | 1 modified |
| 9 | Create CompressionToolProvider | +1 new |
| 10 | Create ICompressionService and implementation | +2 new, 1 modified |
| 11 | Update AgentContextFactory | 1 modified |
| 12 | Update AgentCacheService | 1 modified |
| 13 | Update AgentRunnerAdapter | 1 modified |
| 14 | Register in DI | 1 modified |
| 15 | Add compression events to interface | 1 modified |
| 16 | Implement compression events | 1 modified |
| 17 | Add compression indicator UI | 1 modified |
| 18 | Wire compression events in ChatArea | 1 modified |
| 19 | Final verification | - |

**Total: 4 new files, 16 modified files**
