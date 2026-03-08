# Conversation Reading and Skill Generation Tools Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add two built-in tools that enable AI to read conversation history and generate new skills based on execution patterns.

**Architecture:** Two new tool providers (ConversationToolProvider, SkillGenerationToolProvider) following the existing IToolProvider pattern. ReadConversationData queries ChatMessage, ToolCall, ThinkBlock tables via IConversationRepository and returns a timeline-sorted event list. GenerateSkill validates parameters and writes skill files to `.agents/skills/<skillId>/` directory.

**Tech Stack:** C# / .NET 10, Entity Framework Core, Microsoft.Extensions.AI, System.Text.Json

---

## Task 1: Add Tool Name Constants

**Files:**
- Modify: `SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs`

**Step 1: Add constants for new tools**

Add after the Skills section:

```csharp
    // Conversation analysis (ConversationToolProvider)
    public const string ReadConversationData = nameof(ReadConversationData);

    // Skill generation (SkillGenerationToolProvider)
    public const string GenerateSkill = nameof(GenerateSkill);
```

**Step 2: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs
git commit -m "feat(tools): add ReadConversationData and GenerateSkill constants"
```

---

## Task 2: Create View Models for Conversation Data

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/Conversation/ConversationEventView.cs`
- Create: `SmallEBot/Services/Agent/Tools/Conversation/ConversationDataView.cs`

**Step 1: Create ConversationEventView.cs**

```csharp
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.Conversation;

/// <summary>Represents a single event in the conversation timeline.</summary>
public sealed class ConversationEventView
{
    /// <summary>Event type: user_message, assistant_message, think, or tool_call.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Message or thinking content (for message/think types).</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>ISO8601 timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    /// <summary>Role: user or assistant (for message types only).</summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    /// <summary>Attached file paths (user_message only).</summary>
    [JsonPropertyName("attachedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AttachedPaths { get; init; }

    /// <summary>Requested skill IDs (user_message only).</summary>
    [JsonPropertyName("requestedSkillIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RequestedSkillIds { get; init; }

    /// <summary>Tool name (tool_call only).</summary>
    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; init; }

    /// <summary>Tool arguments JSON (tool_call only).</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; init; }

    /// <summary>Tool result, truncated to 500 chars (tool_call only).</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; init; }
}
```

**Step 2: Create ConversationDataView.cs**

```csharp
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.Conversation;

/// <summary>Complete conversation data returned by ReadConversationData tool.</summary>
public sealed class ConversationDataView
{
    [JsonPropertyName("conversationId")]
    public required string ConversationId { get; init; }

    [JsonPropertyName("events")]
    public required IReadOnlyList<ConversationEventView> Events { get; init; }

    [JsonPropertyName("summary")]
    public required ConversationSummaryView Summary { get; init; }
}

public sealed class ConversationSummaryView
{
    [JsonPropertyName("totalEvents")]
    public required int TotalEvents { get; init; }

    [JsonPropertyName("toolCallCount")]
    public required int ToolCallCount { get; init; }

    [JsonPropertyName("toolUsage")]
    public required Dictionary<string, int> ToolUsage { get; init; }
}
```

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/Conversation/
git commit -m "feat(tools): add conversation data view models"
```

---

## Task 3: Create ConversationToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs`

**Step 1: Create ConversationToolProvider.cs**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Core.Entities;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides conversation data reading tools.</summary>
public sealed class ConversationToolProvider(
    IConversationTaskContext taskContext,
    IConversationRepository repository) : IToolProvider
{
    private const int MaxResultLength = 500;

    public string Name => "Conversation";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ReadConversationData);
    }

    [Description("Read the complete execution history of the current conversation including user messages, assistant messages, thinking blocks, and tool calls. Returns a JSON object with 'events' array sorted by timestamp and 'summary' with statistics. Use this to analyze execution patterns when the user wants to create or improve skills based on conversation history.")]
    private async Task<string> ReadConversationData()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return JsonSerializer.Serialize(new { ok = false, error = "No active conversation context." });

        // Load all data in parallel
        var messages = await repository.GetMessagesForConversationAsync(conversationId.Value);
        var toolCalls = await repository.GetToolCallsForConversationAsync(conversationId.Value);
        var thinkBlocks = await repository.GetThinkBlocksForConversationAsync(conversationId.Value);

        // Build events list
        var events = new List<ConversationEventView>();

        // Add messages
        foreach (var msg in messages.Where(m => m.ReplacedByMessageId == null))
        {
            events.Add(new ConversationEventView
            {
                Type = msg.Role == "user" ? "user_message" : "assistant_message",
                Content = msg.Content,
                Timestamp = msg.CreatedAt.ToString("O"),
                Role = msg.Role,
                AttachedPaths = msg.Role == "user" ? msg.AttachedPaths : null,
                RequestedSkillIds = msg.Role == "user" ? msg.RequestedSkillIds : null
            });
        }

        // Add think blocks
        foreach (var think in thinkBlocks)
        {
            events.Add(new ConversationEventView
            {
                Type = "think",
                Content = think.Content,
                Timestamp = think.CreatedAt.ToString("O")
            });
        }

        // Add tool calls
        foreach (var tc in toolCalls)
        {
            events.Add(new ConversationEventView
            {
                Type = "tool_call",
                Timestamp = tc.CreatedAt.ToString("O"),
                ToolName = tc.ToolName,
                Arguments = tc.Arguments,
                Result = TruncateResult(tc.Result)
            });
        }

        // Sort by timestamp
        var sortedEvents = events.OrderBy(e => e.Timestamp).ToList();

        // Build summary
        var toolUsage = toolCalls
            .GroupBy(tc => tc.ToolName)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new ConversationDataView
        {
            ConversationId = conversationId.Value.ToString(),
            Events = sortedEvents,
            Summary = new ConversationSummaryView
            {
                TotalEvents = sortedEvents.Count,
                ToolCallCount = toolCalls.Count,
                ToolUsage = toolUsage
            }
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private static string? TruncateResult(string? result)
    {
        if (result == null) return null;
        if (result.Length <= MaxResultLength) return result;
        return result[..MaxResultLength] + "... [truncated]";
    }
}
```

**Step 2: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs
git commit -m "feat(tools): add ConversationToolProvider with ReadConversationData"
```

---

## Task 4: Create View Models for Skill Generation

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/SkillGeneration/SkillFileInput.cs`
- Create: `SmallEBot/Services/Agent/Tools/SkillGeneration/GenerateSkillInput.cs`

**Step 1: Create SkillFileInput.cs**

```csharp
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.SkillGeneration;

/// <summary>A file to create in the skill directory.</summary>
public sealed class SkillFileInput
{
    /// <summary>Filename (e.g., 'basic-usage.md' or 'helper.cs').</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>File content.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
```

**Step 2: Create GenerateSkillInput.cs**

```csharp
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.SkillGeneration;

/// <summary>Input for GenerateSkill tool.</summary>
public sealed class GenerateSkillInput
{
    /// <summary>Skill ID in lowercase-hyphen format (e.g., 'my-weekly-report').</summary>
    [JsonPropertyName("skillId")]
    public required string SkillId { get; init; }

    /// <summary>Display name for the skill.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Description for the skill frontmatter (&lt; 1024 chars).</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Main instructions content (markdown body for SKILL.md).</summary>
    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }

    /// <summary>Optional example files to create in examples/ directory.</summary>
    [JsonPropertyName("examples")]
    public IReadOnlyList<SkillFileInput>? Examples { get; init; }

    /// <summary>Optional reference files to create in references/ directory.</summary>
    [JsonPropertyName("references")]
    public IReadOnlyList<SkillFileInput>? References { get; init; }

    /// <summary>Optional script files to create in scripts/ directory.</summary>
    [JsonPropertyName("scripts")]
    public IReadOnlyList<SkillFileInput>? Scripts { get; init; }
}
```

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/SkillGeneration/
git commit -m "feat(tools): add skill generation input view models"
```

---

## Task 5: Create SkillGenerationToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/SkillGenerationToolProvider.cs`

**Step 1: Create SkillGenerationToolProvider.cs**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent.Tools.SkillGeneration;
using SmallEBot.Services.Skills;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides skill generation tools.</summary>
public sealed class SkillGenerationToolProvider(
    ISkillsConfigService skillsConfig) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "SkillGeneration";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GenerateSkill);
    }

    [Description("Create a new skill in .agents/skills/<skillId>/ with SKILL.md and optional examples/references/scripts directories. Parameters: skillId (lowercase-hyphen format), name (display name), description (< 1024 chars), instructions (markdown body), examples/references/scripts (arrays of {filename, content}). Returns { ok, skillPath, filesCreated } on success or { ok: false, error } on failure.")]
    private async Task<string> GenerateSkill(string inputJson)
    {
        GenerateSkillInput? input;
        try
        {
            input = JsonSerializer.Deserialize<GenerateSkillInput>(inputJson, JsonOptions);
        }
        catch
        {
            return JsonSerializer.Serialize(new { ok = false, error = "Invalid JSON input." }, JsonOptions);
        }

        if (input == null)
            return JsonSerializer.Serialize(new { ok = false, error = "Input is required." }, JsonOptions);

        // Validate skillId format
        if (string.IsNullOrWhiteSpace(input.SkillId))
            return JsonSerializer.Serialize(new { ok = false, error = "skillId is required." }, JsonOptions);

        if (!IsValidSkillId(input.SkillId))
            return JsonSerializer.Serialize(new { ok = false, error = "Invalid skillId format. Use lowercase letters, numbers, and hyphens only. Must start with a letter." }, JsonOptions);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(input.Name))
            return JsonSerializer.Serialize(new { ok = false, error = "name is required." }, JsonOptions);

        if (string.IsNullOrWhiteSpace(input.Description))
            return JsonSerializer.Serialize(new { ok = false, error = "description is required." }, JsonOptions);

        if (input.Description.Length > 1024)
            return JsonSerializer.Serialize(new { ok = false, error = "description must be less than 1024 characters." }, JsonOptions);

        if (string.IsNullOrWhiteSpace(input.Instructions))
            return JsonSerializer.Serialize(new { ok = false, error = "instructions is required." }, JsonOptions);

        // Check if skill already exists
        var metadata = await skillsConfig.GetMetadataAsync(CancellationToken.None);
        if (metadata.Any(s => s.Id.Equals(input.SkillId, StringComparison.OrdinalIgnoreCase)))
            return JsonSerializer.Serialize(new { ok = false, error = $"Skill '{input.SkillId}' already exists." }, JsonOptions);

        // Create skill via config service
        try
        {
            var filesCreated = new List<string>();
            var skillDir = await skillsConfig.CreateSkillAsync(input.SkillId, CancellationToken.None);

            // Create SKILL.md
            var skillContent = BuildSkillContent(input);
            await skillsConfig.WriteSkillFileAsync(input.SkillId, "SKILL.md", skillContent, CancellationToken.None);
            filesCreated.Add("SKILL.md");

            // Create examples
            if (input.Examples != null)
            {
                foreach (var ex in input.Examples)
                {
                    var path = $"examples/{ex.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, ex.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            // Create references
            if (input.References != null)
            {
                foreach (var ref in input.References)
                {
                    var path = $"references/{ref.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, ref.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            // Create scripts
            if (input.Scripts != null)
            {
                foreach (var script in input.Scripts)
                {
                    var path = $"scripts/{script.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, script.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                skillPath = $".agents/skills/{input.SkillId}/",
                filesCreated
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"Failed to create skill: {ex.Message}" }, JsonOptions);
        }
    }

    private static bool IsValidSkillId(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        if (!char.IsLetter(skillId[0])) return false;
        return skillId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }

    private static string BuildSkillContent(GenerateSkillInput input)
    {
        return $""""
---
name: {input.Name}
description: {input.Description}
---

# {input.Name}

{input.Instructions}
"""";
    }
}
```

**Step 2: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/SkillGenerationToolProvider.cs
git commit -m "feat(tools): add SkillGenerationToolProvider with GenerateSkill"
```

---

## Task 6: Add Required Methods to ISkillsConfigService

**Files:**
- Modify: `SmallEBot/Services/Skills/ISkillsConfigService.cs`
- Modify: `SmallEBot/Services/Skills/SkillsConfigService.cs`

**Step 1: Check existing interface methods**

Read the existing interface to understand what methods exist.

**Step 2: Add new methods if needed**

Add to interface:
```csharp
/// <summary>Create a new skill directory and return its path.</summary>
Task<string> CreateSkillAsync(string skillId, CancellationToken ct = default);

/// <summary>Write a file to a skill directory.</summary>
Task WriteSkillFileAsync(string skillId, string relativePath, string content, CancellationToken ct = default);
```

**Step 3: Implement in SkillsConfigService**

Add the implementation using the workspace root path.

**Step 4: Commit**

```bash
git add SmallEBot/Services/Skills/ISkillsConfigService.cs SmallEBot/Services/Skills/SkillsConfigService.cs
git commit -m "feat(skills): add CreateSkillAsync and WriteSkillFileAsync methods"
```

---

## Task 7: Register Tool Providers in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add tool provider registrations**

Find the existing tool provider registrations and add:

```csharp
// Tool providers
services.AddScoped<IToolProvider, FileToolProvider>();
services.AddScoped<IToolProvider, SearchToolProvider>();
services.AddScoped<IToolProvider, ShellToolProvider>();
services.AddScoped<IToolProvider, TimeToolProvider>();
services.AddScoped<IToolProvider, TaskToolProvider>();
services.AddScoped<IToolProvider, SkillToolProvider>();
services.AddScoped<IToolProvider, ConversationToolProvider>();      // NEW
services.AddScoped<IToolProvider, SkillGenerationToolProvider>();   // NEW
```

**Step 2: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register ConversationToolProvider and SkillGenerationToolProvider"
```

---

## Task 8: Update System Prompt

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`

**Step 1: Add Conversation section to system prompt**

Add a new section after Task List section:

```csharp
private static string GetConversationSection() => $"""
    # Conversation Analysis

    Tools: `{Tn.ReadConversationData}`.

    Use `{Tn.ReadConversationData}` to analyze the current conversation's execution history when:
    - The user wants to create or improve a skill based on conversation patterns
    - Understanding what tools and approaches worked well
    - Identifying reusable patterns for skill generation

    Returns timeline-sorted events including user messages, assistant responses, thinking blocks, and tool calls with results.
    """;
```

**Step 2: Add Skill Generation section to system prompt**

Add a new section:

```csharp
private static string GetSkillGenerationSection() => $"""
    # Skill Generation

    Tools: `{Tn.GenerateSkill}`.

    Use `{Tn.GenerateSkill}` when the user wants to create a new skill based on analyzed patterns. Parameters:
    - `skillId`: lowercase-hyphen format (e.g., 'my-weekly-report')
    - `name`: display name
    - `description`: what the skill does and when to use it (< 1024 chars)
    - `instructions`: step-by-step guidance (markdown)
    - `examples`: optional array of {{filename, content}}
    - `references`: optional array of {{filename, content}}
    - `scripts`: optional array of {{filename, content}}

    **Workflow for skill creation:**
    1. Call `{Tn.ReadConversationData}` to analyze patterns
    2. Design skill structure based on successful patterns
    3. Call `{Tn.GenerateSkill}` with complete skill definition
    4. Confirm to user where skill was created
    """;
```

**Step 3: Add sections to BuildBaseInstructions**

Add the new section method calls to the `BuildBaseInstructions()` method.

**Step 4: Commit**

```bash
git add SmallEBot/Services/Agent/AgentContextFactory.cs
git commit -m "feat(prompt): add conversation analysis and skill generation sections"
```

---

## Task 9: Build and Test

**Step 1: Build the solution**

```bash
cd D:/RiderProjects/SmallEBot
dotnet build
```

Expected: Build succeeded with 0 errors.

**Step 2: Run the application**

```bash
dotnet run --project SmallEBot
```

**Step 3: Manual test checklist**

- [ ] Start a conversation and make several tool calls
- [ ] Ask "read my conversation data" - verify ReadConversationData returns events
- [ ] Ask "generate a skill based on this" - verify GenerateSkill creates files
- [ ] Check `.agents/skills/<skillId>/` directory exists with correct files
- [ ] Verify SKILL.md has correct YAML frontmatter
- [ ] Restart app and verify skill appears in UI

**Step 4: Fix any issues found during testing**

**Step 5: Commit if any fixes needed**

---

## Task 10: Update Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `README.EN.md`

**Step 1: Add to CLAUDE.md Built-in Tools section**

Add to the table:
```
| `ReadConversationData()` | Timeline of current conversation (messages, tool calls, thinking) |
| `GenerateSkill(...)` | Create new skill from analyzed patterns |
```

**Step 2: Add to README files Features section**

Add:
```
- **Skill generation**: Create new skills based on conversation patterns
```

**Step 3: Commit**

```bash
git add CLAUDE.md README.md README.EN.md
git commit -m "docs: document ReadConversationData and GenerateSkill tools"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | Add tool name constants | BuiltInToolNames.cs |
| 2 | Create conversation view models | Conversation/*.cs |
| 3 | Create ConversationToolProvider | ConversationToolProvider.cs |
| 4 | Create skill generation view models | SkillGeneration/*.cs |
| 5 | Create SkillGenerationToolProvider | SkillGenerationToolProvider.cs |
| 6 | Add ISkillsConfigService methods | ISkillsConfigService.cs, SkillsConfigService.cs |
| 7 | Register tool providers | ServiceCollectionExtensions.cs |
| 8 | Update system prompt | AgentContextFactory.cs |
| 9 | Build and test | - |
| 10 | Update documentation | CLAUDE.md, README*.md |
