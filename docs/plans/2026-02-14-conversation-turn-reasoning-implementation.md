# Conversation Turn and Reasoning Block — Implementation Plan

> **Reference:** [2026-02-14-conversation-turn-reasoning-design.md](./2026-02-14-conversation-turn-reasoning-design.md)

**Goal:** Database-first refactor: one turn = one user message + one AI reply; `ConversationTurn` with `IsThinkingMode`; `TurnId` on all content; reasoning blocks computed by rule (multiple per turn); on AI error persist one error message as that turn's reply and show with error styling.

**Order:** (1) DB + migration + backfill, (2) Model + service (persist + GetMessageGroups), (3) Segmentation helper, (4) UI + error handling.

---

## Task 1: ConversationTurn entity and TurnId on existing entities

**Files:**
- Create: `SmallEBot/Data/Entities/ConversationTurn.cs`
- Modify: `SmallEBot/Data/Entities/Conversation.cs`
- Modify: `SmallEBot/Data/Entities/ChatMessage.cs`
- Modify: `SmallEBot/Data/Entities/ToolCall.cs`
- Modify: `SmallEBot/Data/Entities/ThinkBlock.cs`
- Modify: `SmallEBot/Data/AppDbContext.cs`

**Steps:**

1. **Create ConversationTurn**
   - Properties: `Id` (Guid), `ConversationId` (Guid), `IsThinkingMode` (bool), `CreatedAt` (DateTime).
   - Navigation: `Conversation` (required).

2. **Conversation**
   - Add: `public ICollection<ConversationTurn> Turns { get; set; } = new List<ConversationTurn>();`

3. **ChatMessage**
   - Add: `public Guid? TurnId { get; set; }` and `public ConversationTurn? Turn { get; set; }` (nullable for migration/backfill).

4. **ToolCall**
   - Add: `public Guid? TurnId { get; set; }` (nullable until backfill) and `public ConversationTurn? Turn { get; set; }`.

5. **ThinkBlock**
   - Add: `public Guid? TurnId { get; set; }` (nullable until backfill) and `public ConversationTurn? Turn { get; set; }`.

6. **AppDbContext**
   - `DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();`
   - In `OnModelCreating`: configure `ConversationTurn` (PK, FK to Conversation, cascade delete); configure `ChatMessage.TurnId` (optional FK to ConversationTurn); configure `ToolCall.TurnId` (optional FK); configure `ThinkBlock.TurnId` (optional FK). Add index on `(ConversationId, CreatedAt)` for ConversationTurn if useful.

7. **Build**
   - `dotnet build SmallEBot/SmallEBot.csproj` — must succeed.

8. **Commit**
   - `git add SmallEBot/Data/Entities/ConversationTurn.cs SmallEBot/Data/Entities/Conversation.cs SmallEBot/Data/Entities/ChatMessage.cs SmallEBot/Data/Entities/ToolCall.cs SmallEBot/Data/Entities/ThinkBlock.cs SmallEBot/Data/AppDbContext.cs`
   - `git commit -m "feat(data): add ConversationTurn and TurnId to ChatMessage, ToolCall, ThinkBlock"`

---

## Task 2: EF migration and data backfill

**Files:**
- Add: migration `AddConversationTurns` (or similar name)
- Add: data migration logic (in migration or a one-time backfill step)

**Steps:**

1. **Create migration**
   - From repo root: `dotnet ef migrations add AddConversationTurns --project SmallEBot`
   - Ensure migration adds table `ConversationTurns` and columns `TurnId` (nullable) on `ChatMessages`, `ToolCalls`, `ThinkBlocks`.

2. **Data backfill (in migration or separate SQL/script)**
   - Design: For each conversation, order `ChatMessages` by `CreatedAt`. For each **user** message, create a `ConversationTurn` (ConversationId, IsThinkingMode: true if any ThinkBlock exists in the “reply” segment for that user message, else false, CreatedAt = user message CreatedAt). Then set that Turn.Id on: the user message, and all following assistant messages / tool calls / think blocks until the next user message.
   - Implementation option A: In the same migration, use raw SQL or `migrationBuilder.Sql` to insert turns and update TurnId (complex in EF migrations). Option B: Add a simple C# backfill that runs once on startup if any ChatMessage has null TurnId (e.g. in Program.cs or a middleware), or a one-off console command. Option B is often easier: in the migration only add schema; then in Application code, on first load (or a dedicated endpoint), if `Conversations.Any(c => c.Messages.Any(m => m.TurnId == null))`, run backfill then save. Document in implementation.
   - Recommended: Migration only adds schema. Add a **data migration** helper in `ConversationService` or a small `BackfillTurns` class: load all conversations; for each, walk user messages in order, create turns and assign TurnId to user msg and all following assistant content; save. Call it once from `Program.cs` after `ApplyPendingMigrations` if backfill needed (e.g. check one conversation with null TurnId).

3. **Apply migration**
   - Run app or `dotnet ef database update --project SmallEBot` so DB has new schema.

4. **Commit**
   - `git add SmallEBot/Data/Migrations/*`
   - `git commit -m "feat(data): migration AddConversationTurns and backfill TurnId"`

---

## Task 3: Model — AssistantMessageGroup(Items, IsThinkingMode)

**Files:**
- Modify: `SmallEBot/Models/MessageGroup.cs` (or wherever `AssistantMessageGroup` lives)

**Steps:**

1. **Extend AssistantMessageGroup**
   - Change to: `public sealed record AssistantMessageGroup(IReadOnlyList<TimelineItem> Items, bool IsThinkingMode) : MessageGroup;`
   - All call sites that construct `AssistantMessageGroup` must pass `IsThinkingMode` (next task will provide it from the turn).

2. **Build and fix call sites**
   - Grep for `AssistantMessageGroup(` and update to pass second parameter (e.g. `turn.IsThinkingMode`).

3. **Commit**
   - `git add SmallEBot/Models/MessageGroup.cs` (and any other files that build AssistantMessageGroup)
   - `git commit -m "feat(model): add IsThinkingMode to AssistantMessageGroup"`

---

## Task 4: ConversationService — load turns and build groups from turns

**Files:**
- Modify: `SmallEBot/Services/ConversationService.cs`
- Modify: `SmallEBot/Data/AppDbContext.cs` if Conversation needs to include Turns in GetByIdAsync

**Steps:**

1. **Ensure Conversation loads Turns**
   - In `GetByIdAsync`, include turns: `.Include(c => c.Turns.OrderBy(t => t.CreatedAt))` (or order by CreatedAt when building groups). Ensure Turns are loaded when we load a conversation for the chat page.

2. **Rewrite GetMessageGroups**
   - Signature remains: `public static List<MessageGroup> GetMessageGroups(Conversation conv)` (or instance method that takes conv).
   - Logic: Get `conv.Turns` ordered by `CreatedAt`. For each turn:
     - User message: `conv.Messages.FirstOrDefault(m => m.TurnId == turn.Id && m.Role == "user")`. If null (legacy data), skip or infer from next user message.
     - Assistant items: all `conv.Messages` with `TurnId == turn.Id` and `Role == "assistant"`, all `conv.ToolCalls` with `TurnId == turn.Id`, all `conv.ThinkBlocks` with `TurnId == turn.Id`, merged and ordered by `CreatedAt` into a single timeline (TimelineItem list).
     - Emit `UserMessageGroup(userMessage)` then `AssistantMessageGroup(items, turn.IsThinkingMode)`.
   - If a turn has no user message (should not happen for new data), skip that turn or handle gracefully.

3. **Delete conversation**
   - When deleting a conversation, ensure turns are deleted (cascade from Conversation to ConversationTurn should already remove turns; then cascade or explicit delete of messages/tool/think by TurnId or by ConversationId). Verify delete still works.

4. **Build and test**
   - `dotnet build SmallEBot/SmallEBot.csproj`. Run app and open a conversation that has backfilled turns; confirm groups render (may still show old UI until Task 6).

5. **Commit**
   - `git add SmallEBot/Services/ConversationService.cs SmallEBot/Data/AppDbContext.cs` (if changed)
   - `git commit -m "refactor(service): build message groups from ConversationTurn and pass IsThinkingMode"`

---

## Task 5: AgentService — create turn first, set TurnId, persist error on exception

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`

**Steps:**

1. **Persist flow (success path)**
   - At start of persist (when user sends a message): Create **ConversationTurn**(ConversationId, IsThinkingMode = useThinking from request, CreatedAt = UtcNow). SaveChanges to get Turn.Id (or add to context and get Id).
   - Insert **user** ChatMessage with ConversationId, Role=user, Content, **TurnId = turn.Id**, CreatedAt.
   - Stream and persist assistant content as today: each **ChatMessage** (assistant), **ToolCall**, **ThinkBlock** must get **TurnId = turn.Id**.
   - Ensure `PersistMessagesAsync` (or the method that writes user + assistant content) receives `useThinking` and creates the turn first; then all subsequent writes use the same Turn.Id.

2. **Error path**
   - In the catch block of the send/persist flow (e.g. in ChatArea.Send or in AgentService if it owns the try/catch): if the **turn and user message** have already been created but the stream or persist threw, insert **one** assistant **ChatMessage** with Role=assistant, Content = `"Error: " + ex.Message` (or a sanitized/user-facing string), TurnId = current Turn.Id, CreatedAt = UtcNow. SaveChanges.
   - Ensure the turn is created at the beginning so that on any exception after that, we have Turn.Id available for the error message. If exception happens before turn creation, do not create a turn; optionally show a snackbar and leave the UI without a new bubble.

3. **Build**
   - `dotnet build SmallEBot/SmallEBot.csproj`

4. **Commit**
   - `git add SmallEBot/Services/AgentService.cs`
   - `git commit -m "feat(agent): create turn first, set TurnId on all content, persist error message on AI failure"`

---

## Task 6: Segmentation helper — reasoning block rule (multiple blocks)

**Files:**
- Create or modify: a shared helper (e.g. `SmallEBot/Services/ConversationService.cs` static method, or `SmallEBot/Models/` or `SmallEBot/Services/ReasoningSegmenter.cs`)

**Steps:**

1. **Implement the rule from design §4**
   - Input: ordered list of segments (each is Think | Tool | Text — e.g. from TimelineItem or a DTO).
   - Output: list of **reasoning blocks** (each block = list of Think/Tool in order) and list of **reply segments** (Text | Tool in order for text–tool–text).
   - Algorithm: Walk timeline in order. On **Think** → start new reasoning block, add Think. Inside block: **Think** → add; **Tool** → add, then peek next — if next is not Think, close block; **Text** → close block, add Text to reply segments. **Text** or **Tool** (after block closed) → add to reply segments. Repeat until next **Think** (new block).
   - Expose something like: `public static (List<List<ReasoningStep>> ReasoningBlocks, List<ReplySegment> ReplySegments) SegmentTurn(IReadOnlyList<TimelineItem> items, bool isThinkingMode)`. When `isThinkingMode` is false, return empty reasoning blocks and all items as reply segments (flatten Think as text if needed, or only Text/Tool). Define `ReasoningStep` (Think | Tool) and `ReplySegment` (Text | Tool) as needed.

2. **Unit test (optional but recommended)**
   - Given a timeline [Think, Tool, Think, Text], expect one reasoning block [Think, Tool, Think] and reply [Text]. Given [Think, Tool, Tool, Text], expect one block [Think, Tool, Tool] and reply [Text]. Given [Text, Tool], expect no blocks and reply [Text, Tool]. Given [Think, Tool, Think, Tool, Text, Think, Tool], expect two blocks and reply in between/after.

3. **Build**
   - `dotnet build SmallEBot/SmallEBot.csproj`

4. **Commit**
   - `git add SmallEBot/Services/ReasoningSegmenter.cs` (or where the helper lives)
   - `git commit -m "feat(service): add reasoning block segmentation rule for multiple blocks per turn"`

---

## Task 7: UI — use IsThinkingMode and segmentation; error reply styling

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/wwwroot/app.css` (optional: class for error reply)

**Steps:**

1. **Use IsThinkingMode**
   - For each `AssistantMessageGroup`, read `group.IsThinkingMode` (from the model). Stop inferring from `segments.Any(s => s.IsThink)`.

2. **Run segmentation**
   - For each assistant group, call the segmentation helper with `group.Items` and `group.IsThinkingMode`. Get reasoning blocks + reply segments.

3. **Render**
   - If **IsThinkingMode**: for each reasoning block, render a collapsible “💭 推理过程 (含 N 次工具调用)” (or similar) with the Think/Tool content; then render reply segments in order (text div, tool expansion, …).
   - If **not** IsThinkingMode: render only reply segments (text–tool–text…) with no reasoning panel.

4. **Error reply**
   - If the turn’s assistant content is a single text message and Content starts with a designated prefix (e.g. `"Error: "`), render it with **error styling**: e.g. add a CSS class `smallebot-assistant-error` and style color/background; optionally an icon. Ensure this single-message case is detected (e.g. Items count == 1 and single Message with Role=assistant and Content.StartsWith("Error: ")).

5. **Streaming**
   - When streaming, still pass UseThinkingMode; when persisting, TurnId and turn are already set. On stream error, the catch in Send() should persist the error message for the current turn (Task 5); the UI can show the error in the current bubble immediately and after refresh the error message will appear as the assistant reply.

6. **Build**
   - `dotnet build SmallEBot/SmallEBot.csproj`

7. **Commit**
   - `git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/wwwroot/app.css`
   - `git commit -m "refactor(chat): use IsThinkingMode and segmentation; add error reply styling"`

---

## Task 8: Delete conversation and cascade

**Files:**
- Modify: `SmallEBot/Data/AppDbContext.cs` (ensure cascade)
- Modify: `SmallEBot/Services/ConversationService.cs` (DeleteAsync)

**Steps:**

1. **Cascade**
   - Conversation → ConversationTurn: OnDelete(DeleteBehavior.Cascade). When a conversation is deleted, its turns are deleted. Messages, ToolCalls, ThinkBlocks still have FK to Conversation (and optionally to Turn); if they are deleted by cascade from Conversation, no change. If they are deleted by Turn (TurnId), then deleting the turn would delete them — but we delete the conversation first, so typically we delete by ConversationId. Verify: deleting a conversation removes its messages, tool calls, think blocks, and now turns. No orphan turns.

2. **DeleteAsync**
   - Ensure when we delete a conversation we load and remove or that cascade handles Turns. If EF cascade is set, no code change in DeleteAsync. Otherwise explicitly delete turns for that conversation before deleting the conversation.

3. **Build and commit**
   - `git add SmallEBot/Data/AppDbContext.cs SmallEBot/Services/ConversationService.cs`
   - `git commit -m "chore(data): ensure ConversationTurn cascade on conversation delete"`

---

## Summary

| Task | Description |
|------|-------------|
| 1 | ConversationTurn entity; TurnId on ChatMessage, ToolCall, ThinkBlock; AppDbContext |
| 2 | EF migration AddConversationTurns; data backfill for existing conversations |
| 3 | AssistantMessageGroup(Items, IsThinkingMode) |
| 4 | GetMessageGroups from turns; include Turns in GetByIdAsync |
| 5 | AgentService: create turn first, set TurnId, persist error message on AI failure |
| 6 | Segmentation helper (reasoning block rule, multiple blocks) |
| 7 | ChatArea: IsThinkingMode + segmentation; error reply styling |
| 8 | Cascade delete turns with conversation |

After Task 8, run the app end-to-end: send messages (thinking and non-thinking), trigger an error (e.g. invalid API key), confirm error appears as assistant reply with styling; confirm history shows reasoning blocks and text–tool–text correctly.
