# Tool Call History in LLM Conversations Design

## Overview

Add tool call information to LLM conversation history with truncated results, fix token calculation to reflect actual content, and make truncation configurable via `.agents/agent.json`.

## Requirements

1. Include tool calls in LLM conversation history (not just message text)
2. Truncate tool results to prevent context overflow
3. Single configuration source for truncation limits
4. Token estimation matches actual content sent to LLM
5. Think blocks are NOT included in LLM history

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Tool call format | Hybrid text summary | Tool name + truncated result, simpler than full tool_use/tool_result |
| Truncation limit | 500 characters | Balances context preservation with token efficiency |
| Think blocks | Not included | Saves tokens, LLM regenerates reasoning as needed |
| Configuration | `.agents/agent.json` | Runtime configurable without rebuild |
| Trimming strategy | Keep all messages, truncate tool results only | Simplest approach |

## Architecture

### Configuration

**File:** `.agents/agent.json`

```json
{
  "toolResultMaxLength": 500
}
```

**Service:** `IAgentConfigService`
- Similar pattern to `IModelConfigService`, `ITerminalConfigService`
- Default path: `.agents/agent.json`
- Default `toolResultMaxLength`: 500 if file missing or key absent

### History Building

**Location:** `AgentRunnerAdapter.RunStreamingAsync`

Current flow:
```
Load messages → Trim to fit → Convert to ChatMessage → Send to LLM
```

New flow:
```
Load messages + tool calls → Build ChatMessage with tool summaries → Send to LLM
```

**Tool Call Summary Format:**

For each assistant message, append tool calls from that turn:

```
[Tool: ReadFile(path="test.cs")] → <truncated result>

[Tool: ExecuteCommand(cmd="dotnet build")] → Build succeeded...
```

**Implementation:**

1. Load tool calls: `await repository.GetToolCallsForConversationAsync(conversationId)`
2. Group tool calls by `TurnId`
3. When building assistant `ChatMessage`, append tool summaries for that turn
4. Truncate each tool result to `toolResultMaxLength` characters

### Token Estimation

**Location:** `AgentCacheService.GetEstimatedContextUsageDetailAsync`

Changes:
1. Uncomment tool call loading (line 23)
2. Use `IAgentConfigService` to get `toolResultMaxLength`
3. Pass truncated tool results to `SerializeRequestJsonForTokenCount`
4. Think blocks remain excluded (empty array)

**Updated Method:**

```csharp
public async Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
    Guid conversationId, CancellationToken ct = default)
{
    var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, ct);
    var toolCalls = await conversationRepository.GetToolCallsForConversationAsync(conversationId, ct);

    // Truncate tool results to match what's actually sent
    var maxLength = await agentConfigService.GetToolResultMaxLengthAsync(ct);
    var truncatedToolCalls = toolCalls.Select(t => t with {
        Result = TruncateResult(t.Result, maxLength)
    }).ToList();

    var systemPrompt = agentBuilder.GetCachedSystemPromptForTokenCount()
        ?? FallbackSystemPromptForTokenCount;
    var json = SerializeRequestJsonForTokenCount(systemPrompt, messages, truncatedToolCalls, []);
    // ... rest unchanged
}
```

### ReadConversationData Tool

**Location:** `ConversationToolProvider`

Changes:
1. Inject `IAgentConfigService`
2. Use `GetToolResultMaxLengthAsync()` instead of hardcoded `MaxResultLength = 500`

## Files to Modify

| File | Changes |
|------|---------|
| `SmallEBot/Services/Agent/AgentConfigService.cs` | NEW - config service |
| `SmallEBot/Services/Agent/IAgentConfigService.cs` | NEW - interface |
| `SmallEBot/Extensions/ServiceCollectionExtensions.cs` | Register service |
| `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` | Include tool calls in history |
| `SmallEBot/Services/Agent/AgentCacheService.cs` | Fix token estimation |
| `SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs` | Use config for truncation |

## Error Handling

- Missing `.agents/agent.json`: Use default values (500)
- Invalid JSON: Log warning, use defaults
- Tool result truncation: Append "... [truncated]" when truncated

## Testing Checklist

- [ ] Tool calls appear in LLM history with truncated results
- [ ] Token estimation matches actual content size
- [ ] `ReadConversationData` uses same truncation limit
- [ ] Config changes take effect without restart
- [ ] Missing config file uses defaults gracefully
