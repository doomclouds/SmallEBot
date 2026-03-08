# System Prompt Optimization Design

**Date:** 2026-02-20
**Status:** Approved and implemented
**Based on:** Analysis of Claude Code system prompts (v2.1.41) vs SmallEBot `AgentContextFactory`

---

## 1. Summary

SmallEBot is a **personal assistant**, not a code editor. This design applies Claude Code's prompt architecture selectively — taking behavior rules that apply to any assistant and dropping pure coding-tool rules (git context, OWASP, "read before modify code").

Three structural changes:

1. **Markdown formatting** — all sections use `#` headers, `**bold**` for key rules, backticks for tool names
2. **Section methods** — `BuildBaseInstructions()` delegates to private section methods (Approach B)
3. **Centralized tool name constants** — new `BuiltInToolNames` static class; `GetTimeout()` switch arms updated to use constants instead of magic strings

---

## 2. New Sections Added

| Section | Content |
|---------|---------|
| `# Tone and Style` | No emojis, no colon before tool calls, professional objectivity, no time estimates |
| `# Executing with Care` | Confirm before destructive/external actions; investigate before removing obstacles |
| `# Temporary Files` | Use workspace `temp/` for intermediate files, not `/tmp` |

---

## 3. What Was NOT Changed

- File tools decision tree — already excellent; reformatted only
- Task list workflow — ClearTasks → SetTaskList → CompleteTask already matches Claude Code
- Agentic execution (batching, verification, recovery, scope, progress) — kept as-is
- Skills and terminal blacklist — remain dynamic blocks; only formatting updated

---

## 4. Explicitly Excluded (not applicable to personal assistant)

- Git context injection (branch / status / recent commits)
- "Never propose code changes without reading" (code-editor rule)
- OWASP security rules
- "No backward-compatibility hacks / remove dead code" (code-editor rule)

---

## 5. Files Changed

| File | Change |
|------|--------|
| `SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs` | **New** — centralized tool name constants via `nameof()` pattern |
| `SmallEBot/Services/Agent/AgentContextFactory.cs` | **Rewrite** — Markdown format, section methods, `BuiltInToolNames` references |
| `SmallEBot/Services/Agent/Tools/FileToolProvider.cs` | Minor — `GetTimeout` switch arms use `BuiltInToolNames` |
| `SmallEBot/Services/Agent/Tools/SearchToolProvider.cs` | Minor — `GetTimeout` switch arms use `BuiltInToolNames` |
| `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs` | Minor — `GetTimeout` switch arms use `BuiltInToolNames` |

---

## 6. BuiltInToolNames Pattern

Constants use the `nameof()` self-reference pattern so the identifier name and the string value are always in sync at compile time:

```csharp
public const string ReadFile = nameof(ReadFile); // → "ReadFile"
```

`GetTimeout()` switch arms in tool providers are updated to reference these constants, so any rename triggers a compile error rather than a silent mismatch between the timeout config and the actual registered tool name.
