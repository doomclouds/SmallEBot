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
