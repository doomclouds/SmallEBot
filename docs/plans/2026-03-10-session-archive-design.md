# Session Archive Design (压缩后归档 + 新建 Session)

**Date**: 2026-03-10
**Status**: Implemented
**Goal**: 压缩后创建新 AgentSession，旧 session 增量归档到 session.archives.json，UI 显示 archives + 当前 session 总和。

---

## 需求

1. **压缩后新建 Session**：压缩完成后，当前 session 清空，创建全新的 AgentSession（Agent 从新 session 开始运行）。
2. **归档旧 Session**：被压缩前的消息增量式保存到 `session.archives.json`。
3. **UI 显示**：展示 archives + 当前 session 的合并结果，用户看到完整历史。

---

## 数据流

```
压缩前:
  session.json: [msg0, msg1, ..., msgN]

压缩时:
  1. 生成 summary → metadata.CompressedContext
  2. 将 [msg0..msgN] 追加到 session.archives.json（增量式）
  3. 新建 session.json: 空或仅包含 CompressedContext 的占位（Agent 从新 session 开始）
  4. metadata.EffectiveStartIndex = 0（新 session 从 0 开始）

压缩后:
  session.json: []（新 session，后续消息追加到这里）
  session.archives.json: [{ "messages": [msg0..msgN], "compressedAt": "..." }] 或 扁平数组

UI 加载:
  GetMessagesAsync → 读取 archives 的 messages + session.json 的 messages → 合并返回
```

---

## session.archives.json 格式

**方案 A：按压缩轮次归档（推荐）**
```json
{
  "entries": [
    {
      "compressedAt": "2026-03-10T12:00:00Z",
      "messages": [ /* 该轮压缩前的消息 */ ]
    }
  ]
}
```
每次压缩追加一个 entry。

**方案 B：扁平消息数组**
```json
{
  "messages": [ /* 所有归档消息按时间顺序 */ ]
}
```
每次压缩将本轮消息 append 到 messages 数组。

**推荐 A**：便于按轮次追溯，且与 CompressedAt 对应。

---

## 实现要点

| 组件 | 变更 |
|------|------|
| **ConversationAgentDispatcher.CompactConversationAsync** | 1) 读取当前 session 消息；2) 调用 ArchiveSessionAsync 追加到 archives；3) 清空 session.json 并创建新 session（或调用 agent.CreateSessionAsync 保存空 session） |
| **AgentSessionStore** | 新增 `ArchiveMessagesAsync(conversationId, messages)`，`GetArchivedMessagesAsync(conversationId)`；新增 `CreateNewSessionAsync` 或 `ReplaceWithEmptySessionAsync` |
| **AgentSessionReader** | `GetMessagesAsync` 改为：archives 的 messages + session 的 messages，按时间顺序合并 |
| **CompressedContextProvider** | 逻辑不变，仍从 metadata 注入 CompressedContext；LLM 消息过滤：新 session 的 messages 已不含压缩前消息，无需 EffectiveStartIndex 过滤（或保留以兼容） |
| **ConversationMetadata** | EffectiveStartIndex 在新建 session 后设为 0 或可移除（新 session 天然从 0 开始） |

---

## 新建 Session 的两种方式

1. **Agent 框架：CreateSessionAsync**  
   调用 `agent.CreateSessionAsync()` 得到新 session，用 `SaveAsync` 覆盖 session.json。新 session 的 stateBag 为空。

2. **手动构造空 session.json**  
   写入一个符合 AgentSession 结构的 JSON，其中 `messages` 为空数组。

需确认 Microsoft.Agents.AI 的 AgentSession 结构，以便正确构造空 session。

---

## 待确认

1. **增量式**：每次压缩追加一个 entry，还是覆盖整个 archives？
2. **新 session 的 Load**：Agent 首次 Load 时，若 session.json 为空或仅包含空 messages，应返回 null 还是有效空 session？RunStreamingAsync 会传入新 user message，需要能创建/加载 session。
