# Tool Call History in LLM Conversations Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Include tool calls in LLM conversation history with truncated results, using a unified configuration source.

**Architecture:** Create `IAgentConfigService` for tool result truncation settings. Modify `AgentRunnerAdapter` to include tool call summaries in assistant messages. Fix `AgentCacheService` token estimation to match actual content. Update `ConversationToolProvider` to use shared config.

**Tech Stack:** C# 14, .NET 10, System.Text.Json, DI

---

### Task 1: Create IAgentConfigService Interface

**Files:**
- Create: `SmallEBot/Services/Agent/IAgentConfigService.cs`

**Step 1: Create the interface file**

```csharp
namespace SmallEBot.Services.Agent;

/// <summary>Agent configuration from .agents/agent.json. Provides runtime-configurable settings for agent behavior.</summary>
public interface IAgentConfigService
{
    /// <summary>Maximum length for tool results before truncation. Used by AgentRunnerAdapter for LLM history and ConversationToolProvider for ReadConversationData. Default: 500.</summary>
    Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default);

    /// <summary>Synchronous version for convenience.</summary>
    int GetToolResultMaxLength();
}
```

**Step 2: Build to verify no syntax errors**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/IAgentConfigService.cs
git commit -m "feat(agent): add IAgentConfigService interface for agent configuration"
```

---

### Task 2: Create AgentConfigService Implementation

**Files:**
- Create: `SmallEBot/Services/Agent/AgentConfigService.cs`

**Step 1: Create the implementation file**

```csharp
using System.Text.Json;

namespace SmallEBot.Services.Agent;

/// <summary>Loads agent configuration from .agents/agent.json. Falls back to defaults if file missing or invalid.</summary>
public sealed class AgentConfigService : IAgentConfigService
{
    private const int DefaultToolResultMaxLength = 500;
    private const int MinToolResultMaxLength = 100;
    private const int MaxToolResultMaxLength = 10000;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public AgentConfigService()
    {
        var agentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");
        _filePath = Path.Combine(agentsPath, "agent.json");
    }

    public int GetToolResultMaxLength()
    {
        if (!File.Exists(_filePath))
            return DefaultToolResultMaxLength;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.ToolResultMaxLength ?? 0;
            return raw >= MinToolResultMaxLength
                ? Math.Clamp(raw, MinToolResultMaxLength, MaxToolResultMaxLength)
                : DefaultToolResultMaxLength;
        }
        catch
        {
            return DefaultToolResultMaxLength;
        }
    }

    public async Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultToolResultMaxLength;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.ToolResultMaxLength ?? 0;
            return raw >= MinToolResultMaxLength
                ? Math.Clamp(raw, MinToolResultMaxLength, MaxToolResultMaxLength)
                : DefaultToolResultMaxLength;
        }
        catch
        {
            return DefaultToolResultMaxLength;
        }
    }

    private sealed class AgentConfigFile
    {
        public int ToolResultMaxLength { get; set; } = DefaultToolResultMaxLength;
    }
}
```

**Step 2: Build to verify no syntax errors**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/AgentConfigService.cs
git commit -m "feat(agent): add AgentConfigService implementation"
```

---

### Task 3: Register AgentConfigService in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add registration after other singleton services**

Find the line with `services.AddSingleton<IModelConfigService, ModelConfigService>();` and add after it:

```csharp
services.AddSingleton<IAgentConfigService, AgentConfigService>();
```

**Step 2: Build to verify DI registration**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register IAgentConfigService in DI container"
```

---

### Task 4: Update ConversationToolProvider to Use Config

**Files:**
- Modify: `SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs`

**Step 1: Add IAgentConfigService dependency**

Change constructor from:
```csharp
public sealed class ConversationToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository) : IToolProvider
{
    private const int MaxResultLength = 500;
```

To:
```csharp
public sealed class ConversationToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository,
    IAgentConfigService agentConfig) : IToolProvider
{
```

**Step 2: Update TruncateResult to use config**

Change the `TruncateResult` method from static to instance method:
```csharp
private string? TruncateResult(string? result)
{
    if (result == null) return null;
    var maxLength = agentConfig.GetToolResultMaxLength();
    if (result.Length <= maxLength) return result;
    return result[..maxLength] + "... [truncated]";
}
```

**Step 3: Remove the constant**

Delete the line:
```csharp
private const int MaxResultLength = 500;
```

**Step 4: Build to verify changes**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs
git commit -m "refactor(tools): use IAgentConfigService for truncation in ConversationToolProvider"
```

---

### Task 5: Update AgentRunnerAdapter to Include Tool Calls in History

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add IAgentConfigService dependency**

Change constructor from:
```csharp
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager) : IAgentRunner
```

To:
```csharp
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager,
    IAgentConfigService agentConfig) : IAgentRunner
```

**Step 2: Add helper method for building tool summaries**

Add this method after `ToJsonString`:
```csharp
private string? TruncateToolResult(string? result, int maxLength)
{
    if (result == null) return null;
    if (result.Length <= maxLength) return result;
    return result[..maxLength] + "... [truncated]";
}

private static string BuildToolSummary(IReadOnlyList<Core.Entities.ToolCall> toolCalls, int maxLength)
{
    if (toolCalls.Count == 0) return "";
    var sb = new System.Text.StringBuilder();
    sb.AppendLine();
    sb.AppendLine();
    foreach (var tc in toolCalls)
    {
        var truncatedResult = tc.Result == null
            ? "null"
            : (tc.Result.Length <= maxLength ? tc.Result : tc.Result[..maxLength] + "... [truncated]");
        sb.AppendLine($"\n[Tool: {tc.ToolName}({tc.Arguments})] → {truncatedResult}");
    }
    return sb.ToString();
}
```

**Step 3: Modify RunStreamingAsync to include tool calls**

After line 31 (`var history = await...`), add tool call loading:
```csharp
var history = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, cancellationToken);
var toolResultMaxLength = agentConfig.GetToolResultMaxLength();
var toolCallsByTurn = toolCalls
    .Where(tc => tc.TurnId != null)
    .GroupBy(tc => tc.TurnId!.Value)
    .ToDictionary(g => g.Key, g => (IReadOnlyList<Core.Entities.ToolCall>)g.OrderBy(tc => tc.SortOrder).ToList());
```

**Step 4: Update frameworkMessages building**

Change from:
```csharp
var frameworkMessages = trimResult.Messages
    .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
    .ToList();
```

To:
```csharp
var frameworkMessages = new List<ChatMessage>();
foreach (var m in trimResult.Messages)
{
    var content = m.Content ?? "";
    // Append tool summaries to assistant messages
    if (m.Role == "assistant" && m.TurnId != null && toolCallsByTurn.TryGetValue(m.TurnId.Value, out var turnToolCalls))
    {
        content += BuildToolSummary(turnToolCalls, toolResultMaxLength);
    }
    frameworkMessages.Add(new ChatMessage(ToChatRole(m.Role), content));
}
```

**Step 5: Build to verify changes**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 6: Commit**

```bash
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): include tool call summaries in LLM conversation history"
```

---

### Task 6: Update AgentCacheService to Fix Token Estimation

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentCacheService.cs`

**Step 1: Add IAgentConfigService dependency**

Change constructor from:
```csharp
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer) : IAsyncDisposable
```

To:
```csharp
public class AgentCacheService(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITokenizer tokenizer,
    IAgentConfigService agentConfig) : IAsyncDisposable
```

**Step 2: Add helper method for truncation**

Add after `FormatTokenCount`:
```csharp
private static string? TruncateToolResult(string? result, int maxLength)
{
    if (result == null) return null;
    if (result.Length <= maxLength) return result;
    return result[..maxLength] + "... [truncated]";
}
```

**Step 3: Update GetEstimatedContextUsageDetailAsync**

Change from:
```csharp
public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default)
{
    var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
    // var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);
    // var thinkBlocks = await conversationRepository.GetThinkBlocksForConversationAsync(conversationId, ct);
    var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;
    var json = SerializeRequestJsonForTokenCount(systemPrompt, messages, [], []);
    // ...
}
```

To:
```csharp
public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(Guid conversationId, CancellationToken ct = default)
{
    var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
    var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);
    var toolResultMaxLength = await agentConfig.GetToolResultMaxLengthAsync(ct);

    // Truncate tool results to match what's actually sent to LLM
    var truncatedToolCalls = toolCalls.Select(t => new ToolCallWithTruncatedResult
    {
        ToolName = t.ToolName ?? "",
        Arguments = t.Arguments ?? "",
        Result = TruncateToolResult(t.Result, toolResultMaxLength) ?? ""
    }).ToList();

    var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount() ?? FallbackSystemPromptForTokenCount;
    var json = SerializeRequestJsonForTokenCount(systemPrompt, messages, truncatedToolCalls, []);
    // ... rest unchanged
}
```

**Step 4: Update SerializeRequestJsonForTokenCount signature**

Change from:
```csharp
private static string SerializeRequestJsonForTokenCount(
    string systemPrompt,
    List<Core.Entities.ChatMessage> messages,
    List<Core.Entities.ToolCall> toolCalls,
    List<Core.Entities.ThinkBlock> thinkBlocks)
{
    var payload = new RequestPayloadForTokenCount
    {
        // ...
        ToolCalls = toolCalls.Select(t => new ToolCallItemForTokenCount
        {
            ToolName = t.ToolName ?? "",
            Arguments = t.Arguments ?? "",
            Result = t.Result ?? ""
        }).ToList(),
        // ...
    };
    // ...
}
```

To:
```csharp
private static string SerializeRequestJsonForTokenCount(
    string systemPrompt,
    List<Core.Entities.ChatMessage> messages,
    List<ToolCallWithTruncatedResult> toolCalls,
    List<Core.Entities.ThinkBlock> thinkBlocks)
{
    var payload = new RequestPayloadForTokenCount
    {
        // ...
        ToolCalls = toolCalls.Select(t => new ToolCallItemForTokenCount
        {
            ToolName = t.ToolName,
            Arguments = t.Arguments,
            Result = t.Result
        }).ToList(),
        // ...
    };
    // ...
}
```

**Step 5: Add ToolCallWithTruncatedResult class**

Add after `ThinkBlockItemForTokenCount`:
```csharp
private sealed class ToolCallWithTruncatedResult
{
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Result { get; set; } = "";
}
```

**Step 6: Build to verify changes**

Run: `dotnet build --no-restore`
Expected: Build succeeded with 0 errors

**Step 7: Commit**

```bash
git add SmallEBot/Services/Agent/AgentCacheService.cs
git commit -m "fix(agent): include truncated tool calls in token estimation"
```

---

### Task 7: Final Verification

**Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded with 0 errors, 0 warnings

**Step 2: Run application to verify startup**

Run: `dotnet run --project SmallEBot` (start and then stop with Ctrl+C)
Expected: No startup errors related to DI

**Step 3: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: resolve any remaining issues"
```

---

## Summary

| Task | Description | Files Changed |
|------|-------------|---------------|
| 1 | Create IAgentConfigService interface | +1 new |
| 2 | Create AgentConfigService implementation | +1 new |
| 3 | Register in DI | 1 modified |
| 4 | Update ConversationToolProvider | 1 modified |
| 5 | Update AgentRunnerAdapter with tool summaries | 1 modified |
| 6 | Update AgentCacheService token estimation | 1 modified |
| 7 | Final verification | - |

**Total: 2 new files, 4 modified files**
