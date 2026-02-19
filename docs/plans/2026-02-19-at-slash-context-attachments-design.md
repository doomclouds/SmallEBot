# @ and / Context Attachments — Design

**Date:** 2026-02-19  
**Status:** Draft  
**Summary:** Typing `@` in the chat input opens a popover listing workspace files (allowed extensions); typing `/` opens a popover listing skills. Selected items appear in the input as `@path` and `/skillId`. On send, workspace file contents are injected into the turn’s system context and skill usage is hinted so the model calls ReadSkill etc. to learn.

---

## 1. Overview and scope

**Goal:** Let the user attach workspace files and request skills from the chat input without leaving the page. The model receives file contents in context and is instructed to use the requested skills (by calling ReadSkill / ReadSkillFile as needed).

**In scope:**
- **@** — Popover with a flat, filterable list of workspace files whose extension is in `AllowedFileExtensions`. On selection, insert `@relativePath` into the input. On send, each attached file’s content is injected into this turn’s context (system-side).
- **/** — Popover with a flat list of skills (from existing skills config). On selection, insert `/skillId` into the input. On send, the turn gets a short instruction that the user wants to use those skills; the model is expected to call ReadSkill(skillId) (and related tools) to learn and apply them. No full skill content is injected.
- Multiple @ and multiple / per message are supported; all are applied for that turn only.
- Backend builds the extra context from explicit attachment/skill lists sent by the front-end (no parsing of message text for @ or / on the server).

**Out of scope (for this design):**
- Changing allowed file extensions (still `AllowedFileExtensions`).
- Loading full skill bodies into the system prompt; skills remain progressive (metadata in system, content via tools).

**Success criteria:** User can type @, pick a file, see `@path` in the input, send; the reply is informed by that file’s content. User can type /, pick a skill, see `/skillId`, send; the model is prompted to use that skill and does so via tools.

---

## 2. Data flow and API

**Front-end:** (1) Detect `@` and `/` in the input and open the appropriate popover (workspace files vs skills). (2) Flatten workspace tree to a list of files with allowed extensions; show skills from the existing skills API. (3) On selection, insert `@relativePath` or `/skillId` into the input and close the popover. (4) On send, compute from the current input the lists of all `@path` and `/skillId` tokens (parse or parallel state). (5) Send to the backend: message text, `attachedPaths` (string[]), `requestedSkillIds` (string[]).

**Backend:** Extend the pipeline to accept optional `attachedPaths` and `requestedSkillIds` for this turn. Stored user message remains the single string (including @ and /). The runner uses the two lists only to build a per-turn context fragment and inject it into the request.

**Extension points:**  
- **Application:** `CreateTurnAndUserMessageAsync` and `StreamResponseAndCompleteAsync` add optional parameters `IReadOnlyList<string>? attachedPaths = null`, `IReadOnlyList<string>? requestedSkillIds = null`.  
- **Host:** `IAgentRunner.RunStreamingAsync` gains optional `attachedPaths` and `requestedSkillIds`; the adapter builds the extra fragment and runs the agent with base system prompt + fragment for this turn only.  
- **Blazor:** Send payload includes the two arrays; the component that calls the pipeline passes them through.

**Message text:** Stored and displayed user message is the same string the user typed (including `@path` and `/skillId`). Backend does not re-parse; it uses only the explicit lists.

---

## 3. Backend: per-turn context injection

**Fragment content:** For this turn only we build a text block: (1) **Attached files** — for each path in `attachedPaths`, read content via `IWorkspaceService.ReadFileContentAsync(relativePath)`. If the path is not under the workspace or extension is not allowed, skip or log; do not throw. Format as a clear block, e.g. "User attached the following file(s) for this turn:\n\n--- path ---\n{content}\n\n". (2) **Requested skills** — for each id in `requestedSkillIds`, add a line like "The user wants you to use the skill \"{id}\". Call ReadSkill(\"{id}\") (and ReadSkillFile / ListSkillFiles as needed) to learn and apply it." No file content is read for skills; we only inject this instruction.

**Where to inject:** The agent is built once with a fixed system prompt from `IAgentContextFactory`. There is no per-request system override in the current Agent Framework call. So we inject the fragment by **prepending a single User message** to the message list for this run: the first user message in the list sent to the agent will be "Attached context for this turn:\n\n{fragment}\n\n--- User message below ---\n\n{actual user message}". Thus the model sees the attached files and skill hints before the real user message, and we do not need to change the agent or the SDK.

**Responsibilities:** `AgentRunnerAdapter` (or a small helper it uses) gets the base system prompt from the context factory only for building the cached agent; for each run it receives `attachedPaths` and `requestedSkillIds`. It calls a new service or inline logic: resolve file contents via `IWorkspaceService`, build the skill-hint lines from the list of ids, concatenate into one fragment string. Then build the message list: [history..., synthetic user message with fragment + real user message]. The synthetic message is not persisted to the DB; only the real user message is already stored by `CreateTurnAndUserMessageAsync`.

**Token and safety:** Very large attachments could exceed context. We do not add a hard token limit in this design; optional follow-up: truncate or skip files above a size threshold. All paths must be relative to the workspace root; the workspace service already scopes to the VFS root.

---

## 4. Front-end: @ and / triggers, popover, list source

**Trigger detection:** In the chat input (e.g. `ChatInputBar` or a parent that owns the input value), on input/keyup or when the user types a character: if the character is `@`, open the workspace-files popover; if it is `/`, open the skills popover. Popover is anchored near the caret or below the input. Optional: if the user continues typing after `@` or `/`, use that as a filter prefix (e.g. `@src/` filters to paths starting with "src/").

**Popover UI:** A single reusable component or two small components (one for files, one for skills). Content: a filter text field (optional) and a scrollable list. For **@**: list items are workspace file paths (relative); only include files whose extension is in `AllowedFileExtensions`. For **/**: list items are skill id + name (from `ISkillsConfigService` or equivalent metadata). On item click: insert `@path` or `/skillId` at the current caret position (or append), close the popover, focus back to the input. Escape or click outside closes without inserting.

**List data:** Workspace: use `IWorkspaceService.GetTreeAsync()`, then flatten the tree to a list of nodes where `!IsDirectory` and `AllowedFileExtensions.IsAllowed(Path.GetExtension(node.RelativePath))`. Skills: use the same metadata source as the agent (e.g. `GetMetadataForAgentAsync` or a dedicated list-for-UI API) to get id and name for each skill.

**State for send:** When the user clicks Send, the component (e.g. `ChatArea`) must produce `attachedPaths` and `requestedSkillIds`. Option A: parse the current input string with a simple rule — e.g. `@(\S+)` and `/([a-zA-Z0-9_-]+)` (or a stricter pattern matching known skill ids). Option B: maintain parallel state (when user selects from popover, push to a list of paths and a list of skill ids; when user deletes text, try to keep lists in sync or re-parse). Recommendation: parse on send so we stay in sync with what the user sees; allow path to contain spaces if we use a delimiter (e.g. `@path/to/file.md` only, no spaces in path).

---

## 5. Error handling and edge cases

- **File not found or not allowed:** When building the fragment, if `ReadFileContentAsync` returns null or the path is not in the allowed list, skip that path and optionally append a short line "File {path} could not be loaded." so the model knows. Do not fail the whole turn.
- **Invalid or removed skill id:** If a requested skill id is not in the current skills metadata, still add a line "The user requested skill \"{id}\"; it was not found in the skills list." so the model can respond appropriately. Do not fail the turn.
- **Empty lists:** If both `attachedPaths` and `requestedSkillIds` are null or empty, do not add any synthetic message; keep the existing behaviour (only history + user message).
- **UI: no workspace / no skills:** If the workspace tree is empty or has no allowed files, show an empty list with a short message (e.g. "No files with allowed extensions."). Same for skills: "No skills available."
- **Duplicate @ or /:** If the user selects the same file or skill twice, the parsed lists may contain duplicates. Backend can de-duplicate by path and by skill id before building the fragment.

---

## 6. Next steps

- Implement in order: (1) Backend API and fragment building + injection in runner; (2) Front-end popover and list data; (3) Wire send payload and parsing.
- Optional: document in AGENTS.md the @ and / behaviour and the allowed extensions.
- After implementation, consider: token/size limit for attached files, and richer popover UX (e.g. type-ahead filter after @ or /).

---
