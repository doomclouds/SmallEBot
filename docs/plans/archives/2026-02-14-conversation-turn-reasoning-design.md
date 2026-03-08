# Conversation Turn and Reasoning Block Design

**Status:** Validated (brainstorm)  
**Date:** 2026-02-14  
**Scope:** Database-first refactor so one "turn" = one user message + one AI reply; IsThinkingMode on the turn; explicit reasoning block boundaries (think–tool–think–tool; multiple blocks per turn).

---

## 1. Goal

- Treat **one user message + one AI reply** as a single **conversation turn**. The turn carries **IsThinkingMode** (whether the reply was produced by the thinking model).
- **Database:** Introduce a first-class **ConversationTurn** entity; all content (user message, assistant messages, tool calls, think blocks) for that turn has a **TurnId** FK.
- **Display:** Thinking-mode turns show one or more **reasoning process** blocks (think–tool–think–tool…); non-thinking turns show **text–tool–text…** only. No inference from “has ThinkBlock”; use **IsThinkingMode** and a clear **reasoning block rule**.
- **Reasoning block boundaries:** Computed from timeline order. A reasoning block **starts** at the first **Think** (or at each new Think after a previous block ended); it **ends** when we see (a) **Text**, or (b) **Tool** such that the **next** segment is **not** Think. There can be **multiple** reasoning blocks in one turn (e.g. reasoning1 → text/tool… → reasoning2 → text…).

---

## 2. Database Schema

### 2.1 New Entity: ConversationTurn

| Column           | Type     | Description |
|------------------|----------|-------------|
| Id               | Guid (PK)| |
| ConversationId   | Guid (FK)| Cascade delete with conversation |
| IsThinkingMode   | bool     | Whether this turn used the thinking model |
| CreatedAt        | DateTime | For ordering turns |

- One conversation has many turns. Turns are ordered by CreatedAt (or by the user message’s CreatedAt).

### 2.2 Existing Entities: Add TurnId

- **ChatMessage:** Add nullable **TurnId** (Guid?, FK → ConversationTurn).
  - User message: set TurnId to the turn it starts.
  - Assistant message: set TurnId to the turn it belongs to.
- **ToolCall:** Add **TurnId** (Guid, FK → ConversationTurn), required.
- **ThinkBlock:** Add **TurnId** (Guid, FK → ConversationTurn), required.

Semantics: one turn = one user ChatMessage (with that TurnId) + N assistant ChatMessages + N ToolCalls + N ThinkBlocks, all with the same TurnId.

### 2.3 Persist Flow

1. User sends a message. Create **ConversationTurn**(ConversationId, IsThinkingMode = current request’s UseThinkingMode, CreatedAt = UtcNow). Save to get Turn.Id.
2. Insert **user** ChatMessage with ConversationId, Role=user, Content, **TurnId = Turn.Id**, CreatedAt.
3. Stream and persist assistant content (ChatMessage, ToolCall, ThinkBlock) with **TurnId = Turn.Id**, CreatedAt as today (incrementing baseTime per segment).
4. **On AI execution error** (exception during stream or before any assistant content persisted): for **this turn**, persist a **single assistant ChatMessage** with Role=assistant, **Content = error message** (e.g. `"Error: " + ex.Message` or a user-facing summary), **TurnId = Turn.Id**, CreatedAt = UtcNow. This ensures the turn always has an “AI reply” for that round; the UI will show this as the assistant’s reply (e.g. with error styling). No ToolCall or ThinkBlock for this turn.

No **FirstAssistantMessageId** or similar on Turn; reasoning boundaries are computed from the ordered timeline (see §4).

---

## 3. Migration and Backfill

- Add **ConversationTurn** table and **TurnId** columns (nullable initially on ChatMessage; null for ToolCall/ThinkBlock until backfilled).
- **Data migration:** For each conversation, walk Messages in CreatedAt order. For each **user** message, create a **ConversationTurn**(ConversationId, IsThinkingMode: infer from “this reply has any ThinkBlock” or default false, CreatedAt = user message CreatedAt). Assign that Turn.Id to: that user message, and all subsequent assistant messages / tool calls / think blocks until the next user message. Then set TurnId NOT NULL where applicable (ToolCall, ThinkBlock). ChatMessage.TurnId can remain nullable for old data if needed, or backfill so every message has TurnId.
- New code paths always set TurnId when creating messages/tools/think.

---

## 4. Reasoning Block Rule (No Extra DB Storage)

Boundaries are **computed** when building display from the turn’s ordered timeline (Think, Tool, Text).

**Definitions:**

- **Start of a reasoning block:** The first **Think** in the turn, or any **Think** that appears after a segment that is not part of the current block (e.g. after Text, or after a Tool that was followed by Text or end).
- **End of a reasoning block:** When we see (a) **Text** → block ends **before** this Text; or (b) **Tool** and the **next** segment (if any) is **not** Think → block ends **after** this Tool (this Tool is included in the block).

**Algorithm (walk timeline in order):**

1. For each segment in timeline order (Think | Tool | Text):
2. If segment is **Think**: start a new reasoning block and add this Think to it; continue.
3. If we are inside a reasoning block:
   - Next is **Think** → add to current block; continue.
   - Next is **Tool** → add to current block; **peek next**: if next is not Think (Text, Tool, or end), **close** the current block (this Tool is the last in the block).
   - Next is **Text** → **close** current block (block does not include this Text); this Text starts the “reply” stream (text–tool–text…).
4. If segment is **Text** or a **Tool** that closed a block → it belongs to the reply stream (text–tool–text…).
5. If we see **Think** again after closing a block → go to step 2 (new reasoning block).

Result: multiple reasoning blocks possible per turn; between them and after the last block we have the flat text–tool–text… reply.

---

## 5. Service and Model Layer

- **ConversationService.GetMessageGroups(Conversation):**
  - Load **ConversationTurn** for the conversation (ordered by CreatedAt or user message time).
  - For each turn: load user message (ChatMessage with TurnId and Role=user), then assistant items (ChatMessage Role=assistant, ToolCall, ThinkBlock with same TurnId), ordered by CreatedAt.
  - Emit **UserMessageGroup**(userMessage) and **AssistantMessageGroup**(Items = timeline items for that turn, **IsThinkingMode** = turn.IsThinkingMode).
- **AssistantMessageGroup** model: extend to **AssistantMessageGroup(IReadOnlyList&lt;TimelineItem&gt; Items, bool IsThinkingMode)**.
- **Segmentation:** A shared helper (or part of the service) takes a turn’s Items + IsThinkingMode and returns: list of **reasoning blocks** (each block = list of Think/Tool in order) and list of **reply segments** (Text | Tool in order for text–tool–text). When IsThinkingMode is false, reasoning blocks list is empty; all content is reply segments.

---

## 6. UI (ChatArea)

- **Input:** Groups from GetMessageGroups; each AssistantMessageGroup has Items and **IsThinkingMode**.
- **Rendering:**
  - If **IsThinkingMode**: run the **reasoning block rule** (§4) on Items to get reasoning blocks + reply segments. Render each reasoning block as a collapsible “💭 推理过程 (含 N 次工具调用)” (or similar); then render reply segments in order (text div, tool expansion, text div, …).
  - If **not** IsThinkingMode: render Items in order as text–tool–text… (no “推理过程” panel).
- **Error reply:** When the turn’s assistant content is a single text message that represents an error (e.g. Content starts with a designated prefix like `"Error: "` or we add an **IsError** flag on the message/model), the UI shows it as the AI’s reply with **error styling** (e.g. color, icon) so the user clearly sees the failure for that round.
- **Streaming:** Same rule can be applied to the streamed list (Think/Tool/Text) and UseThinkingMode so the live bubble matches the persisted shape. Persist still writes with TurnId and turn.IsThinkingMode. On stream error, the UI can show the error in the current bubble and the persist layer will have written the error message as the turn’s assistant reply (§2.3 step 4).

---

## 7. Error Handling and Edge Cases

- **AI execution error:** When the AI call throws (e.g. stream failed, API error), the turn and user message are already created. **Persist a default error reply for this turn:** insert **one** assistant **ChatMessage** with Role=assistant, Content = error text (e.g. `"Error: " + ex.Message` or a sanitized user-facing message), TurnId = current Turn.Id. The UI shows this as the AI's reply for that round (e.g. error style or icon). No ToolCall/ThinkBlock for this turn. This keeps "one user message + one AI reply" consistent and avoids an empty assistant bubble.
- **Empty turn (no error path):** If for some reason no assistant content and no error message were persisted (e.g. very early failure before catch), show user message only; assistant bubble can show “…” or nothing until refresh. Prefer always writing the error message in the catch block so this case is rare.
- **Delete conversation:** Cascade delete turns then messages/tool/think (or delete by TurnId when deleting conversation).
- **Old data:** Migration sets TurnId and creates turns; any message without TurnId can be treated as “legacy” and grouped by existing timeline logic for backward compatibility if needed.

---

## 8. Summary

| Area        | Change |
|------------|--------|
| DB         | ConversationTurn; TurnId on ChatMessage, ToolCall, ThinkBlock |
| Persist    | Create turn first; set TurnId on all new messages/tool/think |
| GetMessageGroups | Build groups from turns; AssistantMessageGroup(Items, IsThinkingMode) |
| Reasoning  | Computed from timeline: start at Think, end at Text or Tool-with-no-Think-after; multiple blocks |
| UI         | Branch on IsThinkingMode; segment by rule; reasoning panels + text–tool–text; error reply styling |
| Error      | On AI failure: persist one assistant message with error text for that turn; show as AI reply with error styling |

This design document is the single source of truth for the refactor. Implementation can be split into: (1) DB + migration, (2) Persist + GetMessageGroups + model, (3) UI segmentation and rendering.
