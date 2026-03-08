# Conversations、UserPreferences、Workspaces 领域划分审计

> 最终检查三个领域的划分、子领域划分及领域单一纯粹性。

## 一、领域结构总览

| 领域 | 子领域 | 职责 | 纯粹性 |
|------|--------|------|--------|
| **Conversations** | Metadata, Session, TaskList, TurnContext | 对话 CRUD、会话存储、任务列表、Turn 上下文 | 部分混杂 Agent 依赖 |
| **UserPreferences** | — | 用户偏好、用户名显示 | ✓ 纯粹 |
| **Workspaces** | — | 工作区文件、VFS、上传、监听 | ✓ 纯粹 |

---

## 二、Conversations 领域

### 2.1 当前结构

```
Application.Contracts/Conversations/
├── IConversationService.cs          # 对话 CRUD、Turn 管理
├── ICurrentConversationService.cs   # 当前选中对话 ID（UI 状态）
├── ConversationDto.cs
├── ITurnContextFragmentBuilder.cs   # 每轮上下文提示构建
├── Session/
│   ├── IAgentSessionStore.cs
│   ├── IAgentSessionReader.cs
│   └── IConversationSessionCoordinator.cs
└── TaskList/
    ├── ITaskListService.cs
    ├── IAmbientConversationId.cs
    ├── TaskListData.cs
    └── TaskListChangeEvent.cs

Domain/Conversations/
└── Metadata/
    ├── ConversationMetadata.cs
    ├── TurnInfo.cs
    └── IConversationMetadataRepository.cs

Application/Conversations/
├── ConversationService.cs
└── TurnContext/
    └── TurnContextFragmentBuilder.cs

Infrastructure/Conversations/
├── CurrentConversationService.cs
├── Metadata/
│   ├── ConversationMetadataRepository.cs
│   └── ConversationMetadataPersistence.cs
├── Session/
│   ├── AgentSessionStore.cs
│   ├── AgentSessionReader.cs
│   ├── AgentSessionSerializer.cs
│   └── ConversationSessionCoordinator.cs
└── TaskList/
    ├── TaskListService.cs
    ├── TaskListCache.cs
    └── AmbientConversationId.cs
```

### 2.2 子领域划分评估

| 子领域 | 职责 | 纯粹性 | 建议 |
|--------|------|--------|------|
| **Metadata** | 对话元数据、Turn 信息、仓储 | ✓ 纯粹 | 保持 |
| **Session** | 对话消息存储、加载、截断 | ⚠ 依赖 Agent 框架 | 接口暴露 `AIAgentSession`、`AIAgent`，泄露实现细节；可考虑抽象为「对话消息存储」 |
| **TaskList** | 每对话任务列表、工具上下文 | ✓ 纯粹 | 保持 |
| **TurnContext** | 构建 @ 附件、/ 技能的上下文提示 | ⚠ 依赖 Agents | `TurnContextFragmentBuilder` 依赖 `ISkillsConfigService`，属于 Agent 上下文；可考虑移至 Agents 领域 |

### 2.3 根级组件

| 组件 | 职责 | 归属 |
|------|------|------|
| `IConversationService` | 对话 CRUD、Turn 创建、Replace/Prepare | ✓ Conversations |
| `ICurrentConversationService` | 当前对话 ID、UI 选择状态 | ✓ Conversations（会话选择） |
| `ITurnContextFragmentBuilder` | 为 Agent 构建 per-turn 上下文 | 边界：Conversations vs Agents |

### 2.4 建议调整

1. **Session 子领域**：`IAgentSessionStore`、`IAgentSessionReader`、`IConversationSessionCoordinator` 接口暴露 `Microsoft.Agents.AI` 类型。若要保持 Conversations 不依赖 Agent 框架，需引入领域抽象（如 `IConversationMessageStore`），由 Infrastructure 适配到 AgentSession。**当前可接受**：Session 本质是对话消息的持久化，实现绑定 Agent 框架是合理的；接口在 Application.Contracts，Infrastructure 实现。若未来要支持非 Agent 的对话存储，再抽抽象。

2. **ITurnContextFragmentBuilder**：构建的是「当前 Turn 的上下文提示」，供 Agent 使用。放在 Conversations 表示「对话的 Turn 上下文」；依赖 Skills 表示与 Agent 耦合。**建议**：保留在 Conversations.TurnContext，因为输入（attachedPaths、requestedSkillIds）来自对话 Turn，输出是给 Agent 的提示。跨领域依赖（ISkillsConfigService）可接受。

3. **ICurrentConversationService**：UI 状态服务，放在 Conversations 合理（当前选中的对话）。

---

## 三、UserPreferences 领域

### 3.1 当前结构

```
Application.Contracts/UserPreferences/
├── IUserPreferencesService.cs   # 无状态 CRUD
└── IUserNameDisplayService.cs   # 用户名显示 + UsernameChanged 事件

Domain/UserPreferences/
├── UserPreference.cs
└── IUserPreferenceRepository.cs

Application/UserPreferences/
├── UserPreferencesService.cs
└── UserNameDisplayService.cs

Infrastructure/UserPreferences/
└── UserPreferenceRepository.cs
```

### 3.2 评估

| 项目 | 状态 |
|------|------|
| 领域单一 | ✓ 仅用户偏好 |
| 子领域 | 无，结构简单，无需拆分 |
| 无状态 CRUD | ✓ IUserPreferencesService |
| UI 状态抽离 | ✓ IUserNameDisplayService 独立 |
| 仓储模式 | ✓ 接口在 Domain，实现在 Infrastructure |

**结论**：UserPreferences 领域划分清晰、纯粹，符合 DDD 实践。

---

## 四、Workspaces 领域

### 4.1 当前结构

```
Application.Contracts/Workspaces/
├── IWorkspaceService.cs
├── IVirtualFileSystem.cs
├── IWorkspaceWatcher.cs
├── IWorkspaceUploadService.cs
├── WorkspaceNodeDto.cs
└── WorkspaceChangedEventArgs.cs

Domain/Workspaces/
├── WorkspaceReadOnly.cs         # 静态只读路径规则
└── ValueObjects/
    ├── WorkspaceNode.cs
    └── FilePath.cs

Application/Workspaces/
├── WorkspaceService.cs
└── WorkspaceUploadService.cs

Infrastructure/Workspaces/
├── VirtualFileSystem.cs
└── WorkspaceWatcher.cs
```

### 4.2 评估

| 项目 | 状态 |
|------|------|
| 领域单一 | ✓ 工作区文件系统 |
| 子领域 | 无，VFS/Watcher/Upload 均为工作区能力 |
| IVirtualFileSystem | ✓ 在 Application.Contracts（基础设施抽象） |
| IWorkspaceWatcher | ✓ 在 Application.Contracts |
| WorkspaceReadOnly | 静态类，可接受；或改为 `IWorkspaceReadOnlyPolicy` |
| IWorkspaceRepository | 已删除（审计文档中建议） |

**结论**：Workspaces 领域划分清晰。IVirtualFileSystem、IWorkspaceWatcher 作为基础设施抽象放在 Contracts 符合规范。

### 4.3 可选子领域划分

若未来 Workspaces 扩展，可考虑：

- **FileSystem**：IVirtualFileSystem、读写、树结构
- **Upload**：IWorkspaceUploadService
- **Watcher**：IWorkspaceWatcher

当前规模下保持扁平结构即可。

---

## 五、跨领域依赖检查

| 依赖方向 | 是否合理 |
|----------|----------|
| Conversations → UserPreferences | 无 |
| Conversations → Workspaces | 无 |
| UserPreferences → Conversations | 无 |
| UserPreferences → Workspaces | 无 |
| Workspaces → Conversations | 无 |
| Workspaces → UserPreferences | 无 |
| Conversations.TurnContext → Agents (ISkillsConfigService) | 可接受，Turn 上下文为 Agent 准备 |
| Agents → Conversations | IConversationService（ConversationAgentExecutor） | ✓ 合理 |

---

## 六、总结与建议

### 6.1 领域纯粹性

| 领域 | 纯粹性 | 说明 |
|------|--------|------|
| **Conversations** | 良好 | Session 接口暴露 Agent 类型属实现绑定，可接受；TurnContext 依赖 Skills 为跨领域协作 |
| **UserPreferences** | ✓ 纯粹 | 无跨领域依赖 |
| **Workspaces** | ✓ 纯粹 | 无跨领域依赖 |

### 6.2 子领域划分

| 领域 | 当前子领域 | 建议 |
|------|------------|------|
| **Conversations** | Metadata, Session, TaskList, TurnContext | 保持；结构合理 |
| **UserPreferences** | 无 | 无需拆分 |
| **Workspaces** | 无 | 无需拆分 |

### 6.3 已实施优化（2026-03-08）

1. **IConversationMessageStore**：已引入，封装 `GetMessagesAsync`、`TruncateBeforeIndexAsync`，ConversationAgentExecutor、ConversationService、AgentCacheService 使用该抽象。
2. **ITurnContextFragmentBuilder**：已移至 `Application.Contracts.Agents`、`Application.Agents.TurnContext`，Conversations 领域无 Agent 依赖。
3. **IWorkspaceReadOnlyPolicy**：已替代 `WorkspaceReadOnly` 静态类，WorkspaceService、FileToolProvider、SearchToolProvider 注入使用。

### 6.4 结论

三个领域划分清晰，子领域划分合理，领域单一性良好。Conversations 与 Agents 的边界通过 IConversationService / IConversationAgentExecutor 已明确分离。当前结构可保持，上述优化为可选改进。
