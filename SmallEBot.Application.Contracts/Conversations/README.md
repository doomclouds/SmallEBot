# Conversations Domain

Orchestration, session, compression, task list, and per-turn context for chat conversations.

## Navigation Map

| You want to... | Look at |
|----------------|---------|
| Conversation list, CRUD, streaming | `IAgentConversationService` |
| Session persistence, truncation | `Session/` |
| Context compression (LLM summary) | `Compression/` |
| Task list (UI + tools) | `TaskList/ITaskListService` |
| Per-turn context (@, /) | `ITurnContextFragmentBuilder` (impl: Application/Conversations/TurnContext/) |
| Current conversation (UI selection) | `ICurrentConversationService` |
| Ambient conversation id (tools) | `Context/IConversationTaskContext` |

## Subdomains

- **Session** – AgentSession, persistence, truncate from turn
- **Compression** – Token estimation, threshold, LLM summary
- **Context** – AsyncLocal context for task tools
