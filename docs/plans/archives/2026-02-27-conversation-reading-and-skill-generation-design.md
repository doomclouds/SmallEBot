# Conversation Reading and Skill Generation Tools Design

## Overview

Two new built-in tools that enable the AI to read conversation history data and generate new skills based on execution patterns. This allows users to say things like "generate a better weekly report skill based on the above conversation" and have the AI automatically create a skill.

## Requirements

1. Read current conversation data (messages, tool calls, thinking blocks) in timeline order
2. Generate complete skills with SKILL.md, examples, references, and scripts
3. Directly write skill files to `.agents/skills/<skillId>/`

## Design

### Tool 1: `ReadConversationData`

**Purpose:** Read complete execution history of the current conversation.

**Parameters:** None (automatically gets current conversation context)

**Returns:**

```json
{
  "conversationId": "guid",
  "events": [
    {
      "type": "user_message | assistant_message | think | tool_call",
      "content": "...",
      "timestamp": "ISO8601",
      // Additional fields based on type:
      "role": "user | assistant",  // for messages
      "attachedPaths": ["..."],    // for user_message
      "requestedSkillIds": ["..."], // for user_message
      "toolName": "...",           // for tool_call
      "arguments": "...",          // for tool_call
      "result": "..."              // for tool_call (truncated to 500 chars)
    }
  ],
  "summary": {
    "totalEvents": 25,
    "toolCallCount": 10,
    "toolUsage": {
      "ReadFile": 3,
      "WriteFile": 2,
      "ExecuteCommand": 5
    }
  }
}
```

**Event Types:**

| Type | Description | Additional Fields |
|------|-------------|-------------------|
| `user_message` | User input | `role`, `attachedPaths`, `requestedSkillIds` |
| `assistant_message` | AI response | `role` |
| `think` | Thinking block | - |
| `tool_call` | Tool invocation | `toolName`, `arguments`, `result` |

**Implementation Notes:**

- Events sorted by `CreatedAt` timestamp
- Tool call results truncated to 500 characters to avoid excessive data
- Uses `IConversationTaskContext` to get current conversation ID
- Joins data from `ChatMessage`, `ToolCall`, `ThinkBlock` tables

### Tool 2: `GenerateSkill`

**Purpose:** Create a complete skill structure based on analyzed patterns.

**Parameters:**

```json
{
  "skillId": "string (required, lowercase-hyphen format)",
  "name": "string (required, display name)",
  "description": "string (required, < 1024 chars)",
  "instructions": "string (required, markdown content for SKILL.md body)",
  "examples": [
    {
      "filename": "string (e.g., 'basic-usage.md')",
      "content": "string (markdown content)"
    }
  ],
  "references": [
    {
      "filename": "string (e.g., 'api-reference.md')",
      "content": "string (markdown content)"
    }
  ],
  "scripts": [
    {
      "filename": "string (e.g., 'helper.cs')",
      "content": "string (C# script content)"
    }
  ]
}
```

**Directory Structure Created:**

```
.agents/skills/<skillId>/
├── SKILL.md              (required)
├── examples/
│   └── <filename>        (optional)
├── references/
│   └── <filename>        (optional)
└── scripts/
    └── <filename>        (optional)
```

**SKILL.md Format:**

```markdown
---
name: <name>
description: <description>
---

# <name>

<instructions>
```

**Returns:**

```json
{
  "ok": true,
  "skillPath": ".agents/skills/<skillId>/",
  "filesCreated": ["SKILL.md", "examples/basic-usage.md", "scripts/helper.cs"]
}
```

**Error Cases:**

- Invalid skillId format → `{ "ok": false, "error": "Invalid skillId format. Use lowercase letters, numbers, and hyphens only." }`
- Skill already exists → `{ "ok": false, "error": "Skill '<skillId>' already exists." }`
- Missing required fields → `{ "ok": false, "error": "Missing required field: <field>" }`

## Architecture

### Data Flow

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  User Request   │────▶│ ReadConversation │────▶│  AI Analysis    │
│ "Generate skill"│     │     Data         │     │  (in LLM)       │
└─────────────────┘     └──────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
                                                ┌──────────────────┐
                                                │  GenerateSkill   │
                                                │  (write files)   │
                                                └────────┬─────────┘
                                                         │
                                                         ▼
                                                ┌──────────────────┐
                                                │ .agents/skills/  │
                                                │   <skillId>/     │
                                                └──────────────────┘
```

### Implementation Location

| Component | Location |
|-----------|----------|
| `ConversationToolProvider.cs` | `SmallEBot/Services/Agent/Tools/` |
| Tool registration | Add to `GetTools()` in provider |
| BuiltInToolNames | Add `ReadConversationData`, `GenerateSkill` constants |

### Dependencies

- `IConversationTaskContext` - get current conversation ID
- `IConversationRepository` or direct `SmallebotDbContext` - query database
- `IWorkspaceService` - get workspace root path for skill writing

## Usage Example

**User:** "根据上面的执行流程，生成一个更完善的周报总结生成技能"

**AI Workflow:**

1. Call `ReadConversationData()` to get all events
2. Analyze the events to understand:
   - What data sources were used
   - What tools were called
   - What the output format was
   - What worked well vs. what could be improved
3. Design improved skill structure
4. Call `GenerateSkill()` with:
   ```json
   {
     "skillId": "weekly-report-generator-v2",
     "name": "Weekly Report Generator V2",
     "description": "Generate weekly work reports from YuQue knowledge base...",
     "instructions": "## Instructions\n...",
     "examples": [...],
     "scripts": [...]
   }
   ```
5. Confirm to user: "技能已创建在 `.agents/skills/weekly-report-generator-v2/`"

## Testing Checklist

- [ ] `ReadConversationData` returns all event types correctly
- [ ] Events are sorted by timestamp
- [ ] Tool call results are truncated properly
- [ ] `GenerateSkill` creates correct directory structure
- [ ] SKILL.md has valid YAML frontmatter
- [ ] Existing skill detection works
- [ ] Invalid skillId validation works
- [ ] Skill appears in app UI after creation

## Future Enhancements

1. **Skill update mode**: Allow updating existing skill instead of error
2. **Skill preview mode**: Return content without writing files
3. **Template support**: Predefined templates for common skill patterns
