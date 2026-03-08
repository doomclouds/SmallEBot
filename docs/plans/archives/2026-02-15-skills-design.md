# Skills (Local File-Based) — Design

**Status:** Draft  
**Date:** 2026-02-15  
**Target:** Progressive-disclosure skills: metadata in system prompt, ReadFile tool for content; config and UI under `.agents` (sys.skills + skills), aligned with MCP pattern. Support add and import.

---

## 1. Goal and Concepts

**Goal**  
Provide file-based “skills” to the Agent: each skill is a folder containing `SKILL.md` (with name/description in frontmatter) and optional referenced `.md` files and scripts. The Agent sees only skill metadata in the system prompt (progressive disclosure) and uses a single **ReadFile** tool to load full content when needed. User can add and import skills; system ships read-only meta skills.

**What “skills” are**  
- A **skill** = one directory with at least `SKILL.md` that has **valid YAML frontmatter** (with `name` and `description`). A complete skill must contain frontmatter; if `SKILL.md` is missing or has no valid frontmatter, that directory is **not** a skill and is **not loaded** (not listed, not in system prompt).
- The body of `SKILL.md` and any other files (e.g. referenced `.md`, scripts) are read on demand via ReadFile.
- **Progressive disclosure:** System prompt lists only (id, name, description) per skill; no full SKILL.md body or scripts in the initial context.

**Not in scope**  
- Anthropic API “Beta Skills” (e.g. pptx) are a different feature; this design is for local, file-based skills only.

---

## 2. Directory Layout and Metadata

**Base path**  
All paths use `AppDomain.CurrentDomain.BaseDirectory` (same as MCP and DB).

**Directories**  
- **System meta skills (read-only):** `.agents/sys.skills/`. Each subdirectory is one skill; directory name = skill id (e.g. `weekly-report-generator`). Shipped with the app (content copied to output like `.sys.mcp.json`).
- **User skills:** `.agents/skills/`. Same structure: one subdirectory per skill with `SKILL.md` and optional other files. User can add, edit, delete, or import here.

**Metadata and loading rule**  
- Only directories whose **SKILL.md** has **valid YAML frontmatter** (with `name` and `description`) are treated as skills and loaded. Parsed fields: `name`, `description`.
- If a directory has no `SKILL.md`, or `SKILL.md` has no or invalid frontmatter, it is **not** a skill and is **not loaded** (excluded from list and system prompt).
- No parsing of SKILL.md body or other files for the list; full content is read only when the agent calls ReadFile.

**System prompt (progressive disclosure)**  
- On agent build, scan `.agents/sys.skills/` and `.agents/skills/` for subdirectories that contain a valid `SKILL.md` frontmatter.
- Append a fixed-format block to the system prompt, e.g.:  
  “You have access to the following skills. Each has an id and a short description. To use a skill, call the ReadFile tool with the path to its SKILL.md or other files under that skill’s directory (path is relative to the run directory; ReadFile can read any file under it with allowed extensions).”  
- Then list each skill’s id, name, and description. No full content.

---

## 3. ReadFile Tool

**Single tool:** **ReadFile** — general-purpose file read under the current run directory.

**Parameters**  
- One parameter: path (e.g. `path` or `relativePath`). Path relative to the application run directory (`AppDomain.CurrentDomain.BaseDirectory`).

**Scope**  
- Root: current run directory only. The given path is resolved against this root; the resolved path must stay under it (no `..` escape outside the root).
- Only files with **allowed extensions** may be read (e.g. `.md`, `.cs`, `.py`, `.txt`, `.json`, `.yml`, `.yaml` — configurable allowlist). Other files are rejected.

**Rules**  
- Return file contents as text; on missing file, invalid path, or disallowed extension, return a short error message.
- Do not execute any script; read-only.
- Skills live under `.agents/sys.skills/` and `.agents/skills/`; the agent can read their files by passing paths like `.agents/sys.skills/<id>/SKILL.md` or `.agents/skills/<id>/script.py`, but the tool is not limited to those folders — any file under the run directory with an allowed extension is readable.

---

## 4. Backend Services

**SkillsConfigService (new)**  
- Resolves `.agents` path (same as `McpConfigService`: `BaseDirectory + ".agents"`).
- **List:** Enumerates `sys.skills` and `skills` subdirectories; for each, only if `SKILL.md` has valid frontmatter, returns (Id, Name, Description, IsSystem). Id = directory name. Directories without valid frontmatter are not loaded and do not appear in the list.
- **Get metadata for agent:** Returns the list of (id, name, description) for all skills that have valid frontmatter (i.e. only complete skills are loaded; no enable/disable).
- **User skill add:** Create a new subdirectory under `.agents/skills/` with a minimal `SKILL.md` (name + description). Id = directory name (sanitized).
- **User skill delete:** Remove the skill’s directory under `.agents/skills/` (only user skills). System skills are read-only.
- **Import:** Accept a source (e.g. path to a folder or zip). Copy content into a new subdirectory under `.agents/skills/` and ensure `SKILL.md` exists; id from directory name or user input. The imported folder is only shown as a skill if `SKILL.md` has valid frontmatter (same loading rule).

**AgentService**  
- When building the agent:  
  - Call SkillsConfigService to get (id, name, description) for all skills.  
  - Append the skills block to the system prompt.  
  - Register **ReadFile** tool that resolves paths under `.agents/sys.skills/` and `.agents/skills/`.  
- On config/skills change (add/delete/import), call `InvalidateAgentAsync()` so the next request rebuilds the agent (same as MCP).

---

## 5. UI (Reference: MCP)

**Entry**  
- Toolbar button (e.g. “Skills” or icon) in MainLayout AppBar, next to MCP config. Opens a dialog: “Skills 配置”.

**Dialog content**  
- **List:** All skills (system + user). Columns: name (id), description (truncated), source (system / user). System rows: no edit/delete. User rows: delete, and optionally edit / “view files”. No enable/disable switch.
- **Add:** “Add skill” opens a small form: id (directory name), name, description; creates `.agents/skills/{id}/` and `SKILL.md`.
- **Import:** “Import skill” opens a flow: choose folder or zip → pick or enter id → copy into `.agents/skills/{id}/`. Validate at least `SKILL.md` exists.

**Technical**  
- Dialog injects SkillsConfigService. On add/delete/import, create/delete directories and call AgentService.InvalidateAgentAsync(). Expandable row could show “files in this skill” (list of relative paths) for clarity.

---

## 6. Error Handling and Edge Cases

- **Missing `.agents/sys.skills` or `.agents/skills`:** Treat as empty list; create `.agents/skills` when adding or importing the first user skill.
- **No or invalid frontmatter:** Not a skill; do not load (not in list, not in system prompt). Do not crash. ReadFile can still serve the file by path if the agent requests it.
- **Duplicate id (system and user):** Prefer system; or merge with “system” label in UI and only one entry in system prompt (system wins).
- **Import overwrite:** If id exists, either overwrite or ask; recommend “overwrite” with a short confirmation.
- **ReadFile path escaping:** Reject any path that resolves outside the run directory; return error message.

---

## 7. Summary Table

| Item | Choice |
|------|--------|
| System skills | `.agents/sys.skills/` (read-only, shipped with app) |
| User skills | `.agents/skills/` (read/write) |
| Skill definition | Only directories with valid SKILL.md frontmatter (name, description) are loaded; no frontmatter = not a skill |
| System prompt | Only (id, name, description) for all skills |
| Tool | ReadFile(path) — path relative to run directory; allowed extensions only |
| UI | Toolbar → dialog: list, add, import; user skills deletable (no enable/disable) |
