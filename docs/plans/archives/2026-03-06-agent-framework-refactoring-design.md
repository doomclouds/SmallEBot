# Agent Framework Migration Design

## Overview

This document outlines the comprehensive refactoring of SmallEBot to fully leverage Microsoft Agent Framework's native capabilities, replacing custom implementations with framework-standard patterns.

**Key Goals**:
1. Adopt native `AgentSession` for conversation state management
2. Use file-based session storage in `.agents/sessions/`
3. Integrate Workflow + Checkpoint for branch/regenerate features
4. Simplify UI to directly reflect native event types
5. Remove redundant tools and system prompt sections

---

## Priority Classification

### P0 - Critical (Must Fix First)

| Issue | Current State | Target State |
|-------|---------------|--------------|
| Session Management | None - loads history from DB every request | Native `AgentSession` with serialization |
| Conversation Storage | Complex entities in SQLite | JSON files in `.agents/sessions/` |
| History Rebuilding | Every request loads all messages | Session maintains history internally |

### P1 - Important (After P0 Complete)

| Issue | Current State | Target State |
|-------|---------------|--------------|
| Dialog Branch/Regenerate | Manual entity deletion (300+ lines) | Workflow Checkpoint restore |
| Context Compression | Custom `CompressionService` | Keep but integrate with session persistence |
| Skills System | Custom `SkillToolProvider` | Native `FileAgentSkillsProvider` |

### P2 - Architecture Optimization

| Issue | Current State | Target State |
|-------|---------------|--------------|
| Agent Creation | Singleton cache pattern | `ChatClientAgentOptions` configuration |
| Tool System | Custom `IToolProvider` | Native `AIContextProvider` pattern |

### P3 - Code Quality

| Issue | Current State | Target State |
|-------|---------------|--------------|
| Repository Bloat | 350+ lines, many special-case methods | Simplified CRUD + session serialization |
| Entity Complexity | 4 entities (ChatMessage, ToolCall, ThinkBlock, Turn) | Single SessionJson field |

---

## Architecture Changes

### File Storage Structure

```
.agents/
├── sessions/                          # NEW: Session storage
│   ├── {conversation-id-1}.json       # Per-conversation file
│   ├── {conversation-id-2}.json
│   └── _index.json                    # Index for fast listing (optional)
│
├── vfs/                               # EXISTING: Workspace
├── .mcp.json                          # EXISTING: MCP config
├── .sys.mcp.json
├── terminal.json
└── models.json
```

### Session File Format

```json
{
  "id": "guid-conversation-id",
  "title": "Conversation Title",
  "userName": "user",
  "createdAt": "2026-03-06T10:00:00Z",
  "updatedAt": "2026-03-06T12:00:00Z",
  "compressedContext": "...",
  "compressedAt": "2026-03-06T11:00:00Z",
  "sessionData": {
    // AgentSession serialized state (JsonElement from SerializeSessionAsync)
  }
}
```

### Simplified Data Model

```csharp
// NEW: ConversationMetadata for file storage
public class ConversationMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CompressedContext { get; set; }
    public DateTime? CompressedAt { get; set; }
    public JsonElement? SessionData { get; set; }
}

// REMOVE: These entities become obsolete
// - ChatMessage
// - ToolCall
// - ThinkBlock
// - ConversationTurn
```

### Service Layer Refactoring

```csharp
// NEW: File-based session persistence
public interface ISessionFileService
{
    Task<ConversationMetadata?> LoadAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSummary>> ListAsync(string userName, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSummary>> SearchAsync(string userName, string query, CancellationToken ct = default);
}

public class ConversationSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
}

// NEW: Session runtime management
public interface ISessionManager
{
    Task<AgentSession> GetOrCreateSessionAsync(Guid conversationId, CancellationToken ct = default);
    Task PersistSessionAsync(Guid conversationId, AgentSession session, CancellationToken ct = default);
    Task<ConversationMetadata> CreateConversationAsync(string userName, string title, CancellationToken ct = default);
}

// MODIFY: AgentRunnerAdapter to use session
public interface IAgentRunner
{
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);
}
```

### Agent Builder Changes

```csharp
// BEFORE: Singleton agent with cached tools
public sealed class AgentBuilder
{
    private AIAgent? _agent;
    private AITool[]? _allTools;

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, ...) { ... }
}

// AFTER: Per-conversation agent with session support
public sealed class AgentBuilder
{
    public async Task<AIAgent> CreateAgentAsync(
        AgentSession? session = null,
        CancellationToken ct = default)
    {
        var skillsProvider = new FileAgentSkillsProvider(
            skillPaths: [_skillsPath, _userSkillsPath]);

        return new AnthropicClient(_clientOptions).AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SmallEBot",
            ChatOptions = new()
            {
                Instructions = await BuildSystemPromptAsync(ct),
                Tools = await GetAllToolsAsync(ct)
            },
            AIContextProviders = [skillsProvider]
        });
    }
}
```

---

## Tool System Changes

### Tools to Keep

| Provider | Tools | Notes |
|----------|-------|-------|
| `TimeToolProvider` | `GetCurrentTime`, `GetWorkspaceRoot` | No changes |
| `FileToolProvider` | `ReadFile`, `WriteFile`, `AppendFile`, `CopyFile`, `CopyDirectory`, `ListFiles` | No changes |
| `SearchToolProvider` | `GrepFiles`, `GrepContent` | No changes |
| `ShellToolProvider` | `ExecuteCommand` | No changes |
| `TaskToolProvider` | `SetTaskList`, `ListTasks`, `CompleteTask`, `CompleteTasks`, `ClearTasks` | No changes |
| `SkillGenerationToolProvider` | `GenerateSkill` | Keep - native doesn't have this |

### Tools to Remove

| Provider | Tools | Reason |
|----------|-------|--------|
| `SkillToolProvider` | `ReadSkill`, `ReadSkillFile`, `ListSkillFiles` | Replace with native `FileAgentSkillsProvider` |
| `ConversationToolProvider` | `ReadConversationData` | Session maintains history internally |

### Migration: Skills System

```csharp
// BEFORE: Custom skill tools
public class SkillToolProvider : IToolProvider
{
    // ReadSkill, ReadSkillFile, ListSkillFiles
}

// AFTER: Native FileAgentSkillsProvider
var skillsProvider = new FileAgentSkillsProvider(
    skillPaths: [
        Path.Combine(workspaceRoot, "sys.skills"),
        Path.Combine(workspaceRoot, "skills")
    ],
    options: new FileAgentSkillsProviderOptions
    {
        SkillsInstructionPrompt = """
            You have access to specialized skills.

            <available_skills>
            {skills}
            </available_skills>

            When relevant, load and follow the skill's instructions.
            """
    });
```

**Native tools provided**:
- `load_skill(skillName)` - Load SKILL.md content
- `read_skill_resource(skillName, resourceName)` - Read reference files

---

## UI Simplification

### Current UI Structure (Complex)

```
AssistantBubble
├── SegmentBlock (folded ThinkBlock)
│   ├── Think content
│   ├── ToolCall 1
│   └── ToolCall 2
└── Text content
```

### Simplified UI Structure

```
StreamItem (flat, same-level items)
├── ThinkItem         ← TextReasoningContent
├── ToolCallItem      ← FunctionCallContent + FunctionResultContent
├── TextItem          ← TextContent
└── ApprovalItem      ← FunctionApprovalRequestContent
```

### Components to Remove

- `ReasoningSegmenter` - Complex segmentation logic no longer needed
- `SegmentBlockView` - No longer need segment abstraction
- ThinkBlock folding logic

### Components to Simplify

```csharp
// NEW: Direct content type mapping
public abstract record StreamItemView
{
    public DateTime CreatedAt { get; init; }
}

public record ThinkItemView : StreamItemView
{
    public required string Content { get; init; }
}

public record TextItemView : StreamItemView
{
    public required string Content { get; init; }
}

public record ToolCallItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

public record ApprovalItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
}
```

### Presentation Service Simplification

```csharp
// BEFORE: Complex segmentation
public class ChatPresentationService
{
    // GetStreamingDisplayItems with think/text/tool grouping logic
    // ReasoningSegmenter.SegmentTurn calls
}

// AFTER: Direct mapping
public class ChatPresentationService
{
    public IReadOnlyList<StreamItemView> ConvertStreamUpdates(
        IReadOnlyList<StreamUpdate> updates)
    {
        return updates.Select(ConvertUpdate).ToList();
    }

    private StreamItemView ConvertUpdate(StreamUpdate update) => update switch
    {
        TextStreamUpdate t => new TextItemView { Content = t.Text },
        ThinkStreamUpdate t => new ThinkItemView { Content = t.Text },
        ToolCallStreamUpdate tc => new ToolCallItemView { ... },
        _ => throw new InvalidOperationException()
    };
}
```

---

## System Prompt Changes

### Sections to Remove

```csharp
// DELETE: Conversation analysis section
private static string GetConversationSection() => $"""
    # Conversation Analysis

    Tools: `{Tn.ReadConversationData}`.
    ...
    """;

// DELETE: Skill tools section (replaced by native)
private static string GetSkillsSection() => $"""
    # Skills
    ...
    `{Tn.ReadSkill}`, `{Tn.ReadSkillFile}`, `{Tn.ListSkillFiles}`
    ...
    """;
```

### Sections to Modify

```csharp
// MODIFY: Simplified Skills section
private static string GetSkillsSection() => """
    # Skills

    Skills live under the workspace root in `sys.skills/` (system) and `skills/` (user).

    To use a skill:
    - `load_skill(skillId)` - Load the skill's SKILL.md instructions
    - `read_skill_resource(skillId, relativePath)` - Read reference files

    Do not use generic file tools on skill directories.
    """;
```

---

## Migration Plan (Phase A: Gradual Refactoring)

### Phase 1: Session Layer Introduction

1. Create `ISessionFileService` and implementation
2. Create `ISessionManager` and implementation
3. Add `ConversationMetadata` model
4. Keep existing entities temporarily
5. Migrate `AgentRunnerAdapter` to use session

**Validation**: All existing tests pass, conversations work as before

### Phase 2: Workflow Integration

1. Create `WorkflowAgentRunner` using `InProcessExecution`
2. Implement Checkpoint-based branch/regenerate
3. Create migration tool for existing SQLite data to JSON files
4. Deprecate `ReplaceUserMessageAsync` and `GetTurnForRegenerateAsync`

**Validation**: Branch/regenerate works via checkpoints

### Phase 3: Data Layer Simplification

1. Remove unused entities (`ChatMessage`, `ToolCall`, `ThinkBlock`, `ConversationTurn`)
2. Simplify `IConversationRepository` to basic CRUD
3. Update DI registration

**Validation**: Clean migration, all features work

### Phase 4: UI Simplification

1. Remove `ReasoningSegmenter`
2. Implement flat `StreamItemView` mapping
3. Update Blazor components
4. Remove `SegmentBlockView`

**Validation**: UI displays correctly

### Phase 5: Tool Cleanup

1. Remove `SkillToolProvider`
2. Remove `ConversationToolProvider`
3. Add `FileAgentSkillsProvider` to Agent
4. Update system prompt

**Validation**: All tools work correctly

---

## File Changes Summary

### New Files

| File | Purpose |
|------|---------|
| `Services/Session/ISessionFileService.cs` | Session file persistence interface |
| `Services/Session/SessionFileService.cs` | JSON file implementation |
| `Services/Session/ISessionManager.cs` | Session runtime management |
| `Services/Session/SessionManager.cs` | AgentSession management |
| `Core/Models/ConversationMetadata.cs` | File-based conversation model |
| `Components/Chat/ViewModels/StreamItemView.cs` | Simplified UI view models |

### Modified Files

| File | Changes |
|------|---------|
| `Services/Agent/AgentBuilder.cs` | Use `ChatClientAgentOptions`, add `AIContextProviders` |
| `Services/Agent/AgentRunnerAdapter.cs` | Use `ISessionManager` |
| `Services/Agent/AgentContextFactory.cs` | Remove deleted sections |
| `Components/Chat/Services/ChatPresentationService.cs` | Simplified mapping |
| `Extensions/ServiceCollectionExtensions.cs` | New service registrations |

### Deleted Files

| File | Reason |
|------|--------|
| `Services/Agent/Tools/SkillToolProvider.cs` | Replaced by native |
| `Services/Agent/Tools/ConversationToolProvider.cs` | No longer needed |
| `Infrastructure/Repositories/ConversationRepository.cs` | Simplified to file-based |
| `Core/Entities/ChatMessage.cs` | Obsolete |
| `Core/Entities/ToolCall.cs` | Obsolete |
| `Core/Entities/ThinkBlock.cs` | Obsolete |
| `Core/Entities/ConversationTurn.cs` | Obsolete |
| `Components/Chat/ViewModels/Reasoning/ReasoningSegmenter.cs` | No longer needed |

---

## Risk Mitigation

### Data Migration

1. **Backup before migration**: Export SQLite data before converting
2. **Gradual migration**: Keep SQLite readable during transition
3. **Rollback plan**: Ability to revert to database storage

### UI Compatibility

1. **Parallel rendering**: Support both old and new view models during transition
2. **Component abstraction**: Keep Blazor components decoupled from data source

### Feature Parity

1. **Checklist**: Verify all current features work after each phase
2. **Automated testing**: Add integration tests for critical paths
3. **Manual validation**: User acceptance testing before phase completion

---

## Success Criteria

1. **P0 Complete**: All conversations use `AgentSession`, stored as JSON files
2. **P1 Complete**: Branch/regenerate uses Workflow Checkpoints
3. **P2 Complete**: Agent configuration uses framework patterns
4. **P3 Complete**: Codebase simplified, obsolete code removed

**Final State**:
- 50%+ reduction in entity complexity
- 40%+ reduction in repository code
- Native Agent Framework patterns throughout
- Simplified, flat UI structure
