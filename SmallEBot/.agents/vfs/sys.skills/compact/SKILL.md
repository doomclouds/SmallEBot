---
name: compact
description: Compress conversation history. Use when context usage is high or user requests /compact.
---

# Context Compression

## Workflow

```
CompactContext()
```

## Output

Tool returns:
- `success`: true/false
- `message`: result description
- `compressedCount`: number of messages compressed

## When to Use

- User says: `/compact`, "压缩上下文", "compress context"
- Context usage is high (displayed in UI)
- Before starting a new task in a long conversation

## Notes

- Compressed content is automatically added to system prompt as "Conversation Summary"
- Only messages before the compression timestamp are included
- Tool call is silent - no verbose output needed
