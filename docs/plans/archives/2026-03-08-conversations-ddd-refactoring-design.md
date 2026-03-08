# Conversations 领域 DDD 重构设计

> 基于双文件存储、无数据迁移，统一 Domain 模型，启用 Infrastructure 仓储。

## 一、目标

1. 统一使用 Domain.ConversationMetadata 为唯一聚合根，移除 Core 双模型
2. 双文件存储：metadata.json + session.json，元数据与 Session 解耦
3. 正确写入 FirstMessageIndex，支持精确截断与 Regenerate
4. 删除 SessionFileService、SessionManager，启用 ConversationMetadataRepository、AgentSessionStore

## 二、存储布局

```
.agents/conversations/
└── {conversationId:N}/
    ├── metadata.json    # Domain.ConversationMetadata（不含 SessionData）
    └── session.json     # AgentSession 序列化（仅 Infrastructure 读写）
```

- 新会话直接写入新目录
- 旧 `.agents/sessions/*.json` 不再使用，无需迁移

## 三、Domain 层

### 3.1 聚合根与实体

| 类型 | 说明 |
|------|------|
| **ConversationMetadata** | 聚合根，Id、Title、UserName、CompressedContext、CompressedAt、Turns |
| **TurnInfo** | 实体，Id、CreatedAt、FirstMessageIndex、AttachedPaths、RequestedSkillIds |

### 3.2 TurnInfo 可写性

为支持 FirstMessageIndex 事后写入，需在 TurnInfo 或 ConversationMetadata 上增加：

```csharp
// 方案：ConversationMetadata 上增加
public void SetFirstMessageIndexForTurn(Guid turnId, int index);
```

或 TurnInfo 将 FirstMessageIndex 改为 `private set`，由聚合根通过内部方法更新。

### 3.3 仓储接口

`IConversationMetadataRepository` 已存在于 Domain，保持不变。

## 四、Infrastructure 层

### 4.1 组件职责

| 组件 | 职责 |
|------|------|
| **ConversationMetadataRepository** | 读写 metadata.json，使用 Domain.ConversationMetadata |
| **AgentSessionStore** | 读写 session.json，使用 Microsoft.Agents.AI.AgentSession |
| **AgentSessionReader** | 从 IAgentSessionStore 加载 session 并解析 messages |

### 4.2 序列化

- **metadata.json**：序列化 Domain.ConversationMetadata。若 `_turns` 等私有字段导致默认 JSON 无法反序列化，需采用：
  - 持久化 DTO + 映射，或
  - `[JsonInclude]` / 自定义 JsonConverter
- **session.json**：由 AgentSessionSerializer 负责，保持不变

### 4.3 路径统一

- ConversationMetadataRepository：`.agents/conversations/{id:N}/metadata.json`
- AgentSessionStore：`.agents/conversations/{id:N}/session.json`

两者共用同一 conversation 目录，Delete 时删除整个目录。

## 五、Application 层

### 5.1 新接口 IConversationSessionCoordinator

替代 ISessionAgentManager 的 Session 编排职责：

```csharp
Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
    Guid conversationId, string userName, AIAgent agent, CancellationToken ct);

Task PersistSessionAsync(
    Guid conversationId, AgentSession session, ConversationMetadata metadata,
    AIAgent agent, CancellationToken ct);
```

实现依赖：IConversationMetadataRepository、IAgentSessionStore、AgentSessionSerializer。

### 5.2 FirstMessageIndex 流程

```
CreateTurnAndUserMessageAsync:
    1. IConversationMetadataRepository.GetByIdAsync(conversationId)
    2. metadata.AddTurn(firstMessageIndex: 0, attachedPaths, requestedSkillIds)  // 占位
    3. IConversationMetadataRepository.SaveAsync(metadata)

StreamResponseAndCompleteAsync 开始:
    1. coordinator.GetOrCreateSessionAsync() → (session, metadata)
    2. 从 session 解析 messages.Count（新建 session 则为 0）
    3. metadata.SetFirstMessageIndexForTurn(lastTurnId, messages.Count)
    4. IConversationMetadataRepository.SaveAsync(metadata)
    5. 执行 Agent
    6. coordinator.PersistSessionAsync()
```

### 5.3 AgentConversationService 依赖变更

| 原依赖 | 新依赖 |
|--------|--------|
| ISessionFileService | IConversationMetadataRepository |
| ISessionManager | IConversationSessionCoordinator |
| IAgentSessionReader | IAgentSessionReader（改为从 IAgentSessionStore 读取） |

### 5.4 AgentSessionReader 改造

- 依赖：从 ISessionFileService 改为 IAgentSessionStore（或 IConversationSessionCoordinator 提供 session 内容）
- `GetUserMessageContentAsync(conversationId, turnId)`：通过 metadata 获取 TurnInfo.FirstMessageIndex，再定位 user 消息

## 六、Host 层

### 6.1 删除

| 删除 | 原因 |
|------|------|
| SessionFileService | 由 ConversationMetadataRepository 替代 |
| SessionManager | 由 ConversationSessionCoordinator 替代 |

### 6.2 DI 变更

- 移除：ISessionFileService、SessionManager 注册
- 新增：IConversationSessionCoordinator 注册
- AgentConversationService、AgentRunnerAdapter 注入新依赖

## 七、Core 层清理

| 删除 | 原因 |
|------|------|
| Core.Models.ConversationMetadata | 统一使用 Domain.ConversationMetadata |
| Core.Models.TurnMetadata | 统一使用 Domain.TurnInfo |
| Core.Entities.Conversation | 若仅用于 UI 展示，改为 DTO 或从 Domain 映射 |

注意：Core.Entities.Conversation 可能被 IAgentConversationService 返回，需确认是否改为 Domain 类型或新建 DTO。

## 八、迁移与兼容

- 无数据迁移：旧 sessions 目录保留但不读取
- 新会话一律使用 `.agents/conversations/{id}/` 布局

## 九、参考

- `docs/plans/2026-03-08-conversations-domain-analysis.md`
- `docs/plans/2026-03-07-ddd-restructuring-design.md`
