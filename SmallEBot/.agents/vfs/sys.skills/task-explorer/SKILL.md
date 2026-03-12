---
name: task-explorer
description: Explore codebases, discover files, gather information, and research without making changes. Use when the user asks to explore, search, find, locate, understand structure, analyze layout, or gather context. Ideal for codebase discovery, file pattern finding, documentation lookup, and pre-implementation research.
---

# Task Explorer

Explore-first workflow for discovery and research tasks. Do not write files or run commands that modify state. Gather information and present findings.

## Sub-Agent Usage

- **Simple (handle directly):** Single file lookup, one directory list, one `Grep`/`ReadFile`, or a quick "find X in path Y". No sub-agent.
- **Complex (use `RunSubAgent`):** Multi-directory exploration, cross-referencing many files, large codebase discovery, multi-source research, or analysis spanning multiple areas. Delegate with `identity` describing the explorer role and `task` with the exploration goal.

## When to Use

- User says: "explore", "search", "find", "locate", "what's in", "understand structure", "analyze layout"
- Codebase discovery: "what files handle X?", "where is Y defined?"
- Documentation lookup: "find docs about Z"
- Pre-implementation research: gather context before design or coding

## Tool Flow

### 1. Get workspace root (when needed)

- `GetWorkspaceRoot()` — call once when MCP or scripts need absolute path; reuse result.

### 2. Explore directories

- `ListFiles(path?)` — list direct children. Use for "what is in folder X?".
- Start at `.` or omit path for root; then drill into subdirectories.

### 3. Find files by name/pattern

- `FindBlobs(pattern, mode?, path?, maxDepth?)`
  - `mode "glob"` (default): `*.cs`, `**/*.json`, `*config*`
  - `mode "regex"`: regex matched against relative file paths
  - `maxDepth`: recursion limit (default 10; 0 = unlimited)

### 4. Search content inside files

- `Grep(pattern, path?, filePattern?, filesOnly?, contextLines?, maxResults?, maxDepth?)`
  - `filesOnly=true` — cheapest way to locate where something is defined
  - `contextLines=N` — N surrounding lines per match
  - **Best pattern:** `Grep(pattern, filesOnly=true)` → pick file → `ReadFile(path, startLine, endLine)`

### 5. Read files

- `ReadFile(path, startLine?, endLine?, lineNumbers?)`
  - For large files: use `Grep` first to find target line, then `ReadFile` with `startLine`/`endLine`
  - `lineNumbers=true` — useful when cross-referencing search results

### 6. Delegate complex exploration (when applicable)

- `RunSubAgent(identity?, task)` — delegate when exploration is complex (see Sub-Agent Usage above). Pass `identity` (e.g. "Explorer: discover files and patterns in the codebase") and `task` (the exploration goal).

### 7. Load other skills (when relevant)

- `load_skill(skillName)` — load a skill's instructions for domain-specific guidance
- `read_skill_resource(skillName, resourcePath)` — read skill reference files

## Process (brainstorming-style)

1. **Explore project context** — use tools above to understand structure and locate relevant files.
2. **Ask clarifying questions** — one at a time, if the goal is ambiguous.
3. **Propose 2–3 approaches** — with trade-offs and a recommendation.
4. **Present findings** — structured summary: what exists, where it is, how it relates.

## Do NOT

- `WriteFile`, `AppendFile`, `CopyFile`, `CopyDirectory` — no writes
- `ExecuteCommand` — no shell execution
- `ClearTasks`, `SetTaskList`, `CompleteTask`, `CompleteTasks` — no task list (exploration is single-flow)

## Output

- Concise summary of what was found
- File paths and key locations
- Recommendations or next steps for the user
