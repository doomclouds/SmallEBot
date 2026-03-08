# Conversations 领域：ConversationMetadata、AgentSession、TurnInfo 关系分析

> 为 DDD 重构提供依据，厘清三者职责边界与数据流。

## 一、三者关系概览

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Conversation (会话)                                       │
│  ┌─────────────────────────────────────┐  ┌──────────────────────────────┐ │
│  │ ConversationMetadata (元数据)        │  │ AgentSession (消息内容)        │ │
│  │ - 存储: metadata / 单文件            │  │ - 存储: session.json / 单文件   │ │
│  │ - 聚合根: 标题、压缩、用户、Turns    │  │ - 来源: Microsoft.Agents.AI   │ │
│  │ - Turns: TurnInfo[] / TurnMetadata[] │  │ - 内容: messages[] 扁平数组   │ │
│  └─────────────────────────────────────┘  └──────────────────────────────┘ │
│              │ 1:1 对应                              │                        │
│              │ TurnInfo.FirstMessageIndex ──────────► messages[] 索引         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 核心结论

| 概念 | 职责 | 存储位置 | 当前实现 |
|------|------|----------|----------|
| **ConversationMetadata** | 会话元数据：标题、压缩、用户、轮次列表 | 单文件或 metadata.json | 双模型并存（Core + Domain） |
| **AgentSession** | 消息内容：user/assistant/tool 的完整历史 | SessionData 或 session.json | 单文件内嵌 SessionData |
| **TurnInfo / TurnMetadata** | 每轮元数据：附件、技能、**消息索引** | 内嵌于 Metadata 的 Turns | 缺 FirstMessageIndex |

---

## 二、AgentSession 结构（Microsoft.Agents.AI）

### 序列化形态

```json
{
  "stateBag": {
    "InMemoryChatHistoryProvider": {
      "messages": [
        { "role": "user", "contents": [...] },
        { "role": "assistant", "contents": [text, toolCall, ...] },
        { "role": "tool", "contents": [...] },
        { "role": "assistant", "contents": [...] }
      ]
    }
  }
}
```

### 特点

- **扁平数组**：`messages[]` 按时间顺序排列，无显式 turn 边界
- **单轮可多消息**：user → assistant → tool1 → result1 → assistant → tool2 → result2 → assistant
- **索引语义**：`messages[i]` 的 `i` 即“消息索引”，不能简单用 `turnIndex * 2` 推算

### 当前 AgentSessionReader 的简化假设

```csharp
// GetUserMessageContentAsync: turnIndex → messageIndex
var messageIndex = turnIndex * 2;  // 仅适用于无 tool call 的简单轮次
```

- 有 tool call 时，该公式错误
- 正确做法：用 `TurnInfo.FirstMessageIndex` 直接记录每轮 user 消息的索引

---

## 三、TurnInfo vs TurnMetadata

### Domain.TurnInfo（有 FirstMessageIndex）

```csharp
public class TurnInfo : IEntity<Guid>
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public int FirstMessageIndex { get; init; }   // ← 关键：messages[] 中的索引
    public string[] AttachedPaths { get; init; }
    public string[] RequestedSkillIds { get; init; }
}
```

- 语义：`FirstMessageIndex` 指向该轮 user 消息在 `messages[]` 中的位置
- 用途：截断、重试、Regenerate 时精确定位

### Core.TurnMetadata（无 FirstMessageIndex）

```csharp
public class TurnMetadata
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> AttachedPaths { get; set; }
    public List<string> RequestedSkillIds { get; set; }
}
```

- 当前实际使用：只存附件、技能
- 问题：无法做基于消息索引的精确截断

### 何时设置 FirstMessageIndex？

当前流程：

1. `CreateTurnAndUserMessageAsync`：先创建 TurnMetadata 并保存
2. `StreamResponseAndCompleteAsync`：调用 `AgentRunnerAdapter.RunStreamingAsync`
3. `SessionManager.GetOrCreateSessionAsync`：加载 session，此时已有 `messages.Count`
4. Agent 执行：追加 user + assistant 到 session
5. `PersistSessionAsync`：持久化 metadata + SessionData

因此：**FirstMessageIndex 应在“开始本轮 agent 执行前”确定**，即 `messages.Count`（本轮 user 消息即将写入的索引）。

---

## 四、当前存储与依赖现状

### 存储方式

| 组件 | 路径 | 内容 |
|------|------|------|
| **SessionFileService**（在用） | `.agents/sessions/{id}.json` | 单文件：`ConversationMetadata` + `SessionData` |
| **ConversationMetadataRepository**（未用） | `.agents/conversations/{id}/metadata.json` | 仅 metadata |
| **AgentSessionStore**（未用） | `.agents/conversations/{id}/session.json` | 仅 session |

### 依赖关系

```
AgentConversationService
    ├── ISessionFileService (SessionFileService)     ← 实际使用
    ├── ISessionManager (SessionManager)             ← 实际使用
    ├── IAgentSessionReader (AgentSessionReader)     ← 解析 SessionData
    └── ...

SessionManager
    └── ISessionFileService

AgentSessionReader
    └── ISessionFileService

IConversationMetadataRepository、IAgentSessionStore 已注册但未注入
```

### 双模型并存

| 模型 | 位置 | 使用方 | 特点 |
|------|------|--------|------|
| Core.Models.ConversationMetadata | Core | SessionFileService, SessionManager, AgentConversationService | 含 SessionData、TurnMetadata，无 FirstMessageIndex |
| Domain.Conversations.ConversationMetadata | Domain | 无（仅 IConversationMetadataRepository 接口） | 含 TurnInfo、FirstMessageIndex，无 SessionData |

---

## 五、DDD 重构要点

### 1. 领域边界

- **Conversation 聚合根**：管理会话元数据（标题、压缩、用户、Turns）
- **AgentSession**：基础设施关注点，不进入 Domain；由 Infrastructure 负责序列化/反序列化
- **TurnInfo**：聚合内实体，必须包含 `FirstMessageIndex` 以支持截断和精确定位

### 2. 职责划分

| 层 | 职责 |
|----|------|
| **Domain** | `ConversationMetadata` 聚合根、`TurnInfo` 实体、`IConversationMetadataRepository` |
| **Infrastructure** | 元数据持久化、AgentSession 持久化、SessionData ↔ AgentSession 转换 |
| **Application** | 编排：加载元数据 → 加载 Session → 运行 Agent → 更新 TurnInfo → 持久化 |

### 3. FirstMessageIndex 的写入时机

```
CreateTurnAndUserMessageAsync:
    1. 加载 metadata
    2. 创建 TurnInfo（此时 FirstMessageIndex 未知，可先占位）

StreamResponseAndCompleteAsync 开始:
    1. 加载 metadata
    2. 加载 session（或新建）
    3. firstMessageIndex = session.messages.Count
    4. 更新最后一轮 TurnInfo.FirstMessageIndex = firstMessageIndex
    5. 保存 metadata
    6. 执行 Agent（追加 user + assistant）
    7. 持久化 session
```

或：在 `PersistSessionAsync` 中，由 Infrastructure 在持久化前根据当前 session 状态更新 metadata 中最后一轮的 FirstMessageIndex。

### 4. 存储策略选择

- **方案 A：单文件**（metadata + SessionData）  
  - 优点：实现简单，迁移成本低  
  - 缺点：Domain 需间接依赖 SessionData 的持久化形态（或通过 Infrastructure 抽象）

- **方案 B：双文件**（metadata.json + session.json）  
  - 优点：元数据与 Session 解耦，符合 DDD 分层  
  - 缺点：需协调两处存储、迁移现有数据

### 5. 移除死代码

- `IConversationMetadataRepository`、`AgentSessionStore` 若继续不用，应删除或明确迁移路径
- 若采用方案 B，需将 `SessionManager` 从 SessionFileService 迁移到 `AgentSessionStore` + metadata 仓储

---

## 六、推荐重构路径

1. **统一 Turn 模型**：在 Domain 中只保留 `TurnInfo`（含 FirstMessageIndex），废弃 Core 的 `TurnMetadata`。
2. **统一 Metadata 模型**：以 Domain.ConversationMetadata 为唯一聚合根，移除 Core 的 ConversationMetadata。
3. **明确 FirstMessageIndex 写入**：在 Agent 执行前或 PersistSession 时，根据 session.messages 更新 TurnInfo。
4. **统一存储**：选定单文件或双文件，并迁移到该方案。
5. **修复 AgentSessionReader**：用 `TurnInfo.FirstMessageIndex` 替代 `turnIndex * 2` 获取 user 消息。

---

## 七、参考

- `docs/plans/2026-03-07-ddd-restructuring-design.md`
- `SmallEBot.Domain/Conversations/ConversationMetadata.cs`
- `SmallEBot.Core/Models/ConversationMetadata.cs`
- `SmallEBot/Services/Session/AgentSessionReader.cs`
