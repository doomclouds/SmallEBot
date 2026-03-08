# Agents Domain: Services Migration Brainstorm

**Date:** 2026-03-08  
**Goal:** Migrate all content from `SmallEBot/Services` out of Host. Agents-related content → Agents domain, refactor. Non-Agents content → most appropriate location. Strict DDD layering.

### 已定决策（待审核）

| 决策 | 说明 |
|------|------|
| **AgentBuilder** | 放在 Host 作为 Agent SDK 组合根；在项目根目录创建 **Sdk** 文件夹（无 Services） |
| **Sdk 职责** | 仅提供基础功能：调用运行 Agent、修改配置、运行子代理等 |
| **ToolProviders** | 实现移至**基础设施** Infrastructure/Agents/Tools/ |
| **SkillGeneration** | 随 SkillGenerationToolProvider 置于 Infrastructure/Agents/Tools/ |
| **Infrastructure/Agents** | 增加 Agents 子文件夹（已存在），迁移时增加 Config/, Mcp/, Skills/, Tools/ 子文件夹 |

---

## 1. Current Structure: SmallEBot/Services

### 1.1 Full Tree

```
SmallEBot/Services/
├── Agent/
│   ├── AgentBuilder.cs
│   ├── AgentCacheService.cs
│   ├── AgentConfigService.cs
│   ├── AgentContextFactory.cs
│   ├── AgentRunnerAdapter.cs
│   ├── CompressionService.cs
│   ├── IMcpConnectionManager.cs
│   ├── McpConnectionManager.cs
│   ├── McpToolFactory.cs
│   ├── ModelConfigService.cs
│   ├── TurnContextProvider.cs
│   └── Tools/
│       ├── BuiltInToolNames.cs
│       ├── FileToolProvider.cs
│       ├── IToolProvider.cs
│       ├── IToolProviderAggregator.cs
│       ├── SearchToolProvider.cs
│       ├── ShellToolProvider.cs
│       ├── SkillGenerationToolProvider.cs
│       ├── TaskToolProvider.cs
│       ├── TimeToolProvider.cs
│       ├── ToolProviderAggregator.cs
│       └── SkillGeneration/
│           ├── GenerateSkillInput.cs
│           └── SkillFileInput.cs
├── Circuit/
│   ├── CircuitContextHandler.cs
│   ├── CurrentCircuitAccessor.cs
│   ├── ICurrentCircuitAccessor.cs
│   └── README.md
├── Mcp/
│   ├── McpConfigService.cs
│   └── McpToolsLoaderService.cs
├── Presentation/
│   ├── KeyboardShortcutService.cs
│   └── MarkdownService.cs
├── Skills/
│   ├── SkillFrontmatterParser.cs
│   └── SkillsConfigService.cs
├── Streaming/
│   └── ChannelStreamSink.cs
└── Terminal/
    ├── CommandConfirmResult.cs
    ├── CommandRunner.cs
    ├── ICommandRunner.cs
    ├── ITerminalConfigService.cs
    └── TerminalConfigService.cs
```

### 1.2 Domain Classification

| Subfolder | Domain | Rationale |
|-----------|--------|-----------|
| **Agent** | **Agents** | Core Agent runtime: Builder, Runner, Tools, MCP, Compression, TurnContext |
| **Circuit** | **Host/UI** | Blazor Circuit context (CircuitId), UI-bound for command confirmation |
| **Mcp** | **Agents** | MCP config and tool loading for Agent |
| **Presentation** | **Host/UI** | Keyboard shortcuts, Markdown rendering — pure UI |
| **Skills** | **Agents** | Skill config, frontmatter parsing — Agent context |
| **Streaming** | **Agents** | `ChannelStreamSink` implements `IStreamSink` for Agent output |
| **Terminal** | **Tools/Infrastructure** | Command execution, terminal config — used by ShellToolProvider |

---

## 2. Agents 子领域划分（多文件夹结构）

Agents 领域较复杂，按职责划分为多个子领域，便于导航和职责清晰。

### 2.0 子领域总览

| 子领域 | 职责 | 对应 Host Services |
|--------|------|--------------------|
| **Config** | Agent 配置、模型配置、终端配置、持久化 | AgentConfigService, ModelConfigService, TerminalConfigService |
| **Compression** | 上下文压缩、Token 估算、阈值 | CompressionService |
| **Mcp** | MCP 连接、配置、工具加载 | McpConnectionManager, McpConfigService, McpToolsLoaderService |
| **Skills** | Skill 配置、frontmatter 解析 | SkillsConfigService, SkillFrontmatterParser |
| **Tools** | 工具提供者、工具聚合、命令执行 | Infrastructure/Agents/Tools/: ToolProviderAggregator、FileToolProvider、SkillGeneration、CommandRunner 等 |
| **Execution** | Agent 调度、流式响应 | Application: ConversationAgentDispatcher（调度器）；AgentRunnerAdapter（执行层） |
| **TurnContext** | 每轮上下文构建 | TurnContextProvider, TurnContextFragmentBuilder |
| **Streaming** | 流式输出接收器 | ChannelStreamSink |

### 2.0.1 目标文件夹结构（按层）

```
Application.Contracts/Agents/
├── Config/
│   ├── IAgentConfigService.cs
│   ├── IModelConfigService.cs
│   └── ITerminalConfigService.cs
├── Compression/
│   ├── ICompressionService.cs
│   ├── ICompressionThresholdProvider.cs
│   ├── IContextUsageEstimator.cs
│   └── IToolResultMaxProvider.cs
├── Mcp/
│   └── IMcpConnectionManager.cs
├── Skills/
│   └── ISkillsConfigService.cs
├── Tools/
│   ├── IToolProvider.cs
│   └── IToolProviderAggregator.cs
├── Execution/
│   ├── IAgentRunner.cs
│   └── IConversationAgentDispatcher.cs
├── TurnContext/
│   └── ITurnContextFragmentBuilder.cs
└── Streaming/
    └── IStreamSink.cs

Application/Agents/
├── Compression/
│   └── CompressionService.cs
├── Execution/
│   └── ConversationAgentDispatcher.cs
└── TurnContext/
    └── TurnContextFragmentBuilder.cs

Infrastructure/Agents/
├── Config/
│   ├── AgentConfigRepository.cs
│   ├── AgentConfigService.cs
│   ├── ModelConfigService.cs
│   └── TerminalConfigService.cs
├── Mcp/
│   ├── McpConnectionManager.cs
│   ├── McpConfigService.cs
│   └── McpToolsLoaderService.cs
├── Skills/
│   ├── SkillsConfigService.cs
│   └── SkillFrontmatterParser.cs
└── Tools/
    ├── CommandRunner.cs
    ├── ToolProviderAggregator.cs
    ├── FileToolProvider.cs
    ├── SearchToolProvider.cs
    ├── ShellToolProvider.cs
    ├── TaskToolProvider.cs
    ├── TimeToolProvider.cs
    ├── SkillGenerationToolProvider.cs
    ├── SkillGeneration/
    │   ├── GenerateSkillInput.cs
    │   └── SkillFileInput.cs
    └── BuiltInToolNames.cs

Domain/Agents/
├── Config/
│   ├── AgentConfig.cs
│   ├── IAgentConfigRepository.cs
│   └── ValueObjects/
│       ├── ModelConfig.cs
│       ├── McpServerConfig.cs
│       ├── SkillConfig.cs
│       ├── TerminalConfig.cs
│       └── ToolSet.cs
└── Services/
    ├── IToolProvider.cs
    ├── IToolRegistry.cs
    └── ISubAgentRunner.cs

（注：Domain 现有 ValueObjects/、Services/ 在根下，可先保持；Config 子领域可后续将 AgentConfig、IAgentConfigRepository 移入 Config/）

Host/  （项目根目录）
└── （无 Sdk）Agent 基础能力已移至 Application/Agents/
```

**说明：**
- **Application/Agents/**：AgentBuilder、AgentRunnerAdapter、AgentCacheService、AgentContextFactory、TurnContextProvider、ChannelStreamSink、McpToolFactory 等
- **Infrastructure/Agents/Tools/**：工具提供者实现置于基础设施，含 ToolProviderAggregator、FileToolProvider、SearchToolProvider、SkillGenerationToolProvider、SkillGeneration/ 等

### 2.0.2 Infrastructure/Agents 子文件夹

Infrastructure 下增加 Agents 子文件夹，当前已有 `Agents/AgentConfigRepository.cs`。迁移后结构：

```
Infrastructure/Agents/
├── Config/           ← AgentConfigRepository, AgentConfigService, ModelConfigService, TerminalConfigService
├── Mcp/              ← McpConnectionManager, McpConfigService, McpToolsLoaderService
├── Skills/           ← SkillsConfigService, SkillFrontmatterParser
└── Tools/            ← CommandRunner, ToolProviderAggregator, FileToolProvider, SearchToolProvider, ShellToolProvider, TaskToolProvider, TimeToolProvider, SkillGenerationToolProvider, SkillGeneration/
```

### 2.0.3 子领域导航说明

| 用户想看... | 去哪个子领域 |
|-------------|--------------|
| Agent 配置、模型、终端 | Config |
| 上下文压缩、Token 估算 | Compression |
| MCP 连接、工具加载 | Mcp |
| Skill 配置、frontmatter | Skills |
| 工具提供者、命令执行 | Tools |
| 对话执行、流式响应 | Execution |
| 每轮 @、/ 上下文 | TurnContext |
| 流式输出到 UI | Streaming |

---

## 2.1 Agents Domain (Application.Contracts + Application + Infrastructure + Host)

**Application.Contracts/Agents/** (按子领域分文件夹)

| 子领域 | 接口 | 当前位置 | 操作 |
|--------|------|----------|------|
| Config | `IAgentConfigService`, `IModelConfigService`, `ITerminalConfigService` | Contracts / Host | 移至 Config/ |
| Compression | `ICompressionService`, `ICompressionThresholdProvider`, `IContextUsageEstimator`, `IToolResultMaxProvider` | Contracts | 已在 Compression/ |
| Mcp | `IMcpConnectionManager` | Host | 移至 Mcp/ |
| Skills | `ISkillsConfigService` | Contracts | 移至 Skills/ |
| Tools | `IToolProvider`, `IToolProviderAggregator` | Host / Domain | 接口 → Contracts/Agents/Tools/；实现 → Infrastructure/Agents/Tools/ |
| Execution | `IAgentRunner`, `IConversationAgentDispatcher` | Contracts | 移至 Execution/ |
| TurnContext | `ITurnContextFragmentBuilder` | Contracts | 移至 TurnContext/ |
| Streaming | `IStreamSink` | Contracts | 移至 Streaming/ |

**Application/Agents/** (按子领域)

| 子领域 | 实现 | 当前位置 | 操作 |
|--------|------|----------|------|
| Compression | `CompressionService` | Host | **Move** → Application/Agents/Compression/ |
| Execution | `ConversationAgentDispatcher` | Application | 移至 Execution/ |
| TurnContext | `TurnContextFragmentBuilder` | Application | 已在 TurnContext/ |

**Infrastructure/Agents/** (按子领域)

| 子领域 | 实现 | 当前位置 | 操作 |
|--------|------|----------|------|
| Config | `AgentConfigRepository`, `AgentConfigService`, `ModelConfigService`, `TerminalConfigService` | Infrastructure / Host | 统一至 Config/ |
| Mcp | `McpConnectionManager`, `McpConfigService`, `McpToolsLoaderService` | Host | **Move** → Mcp/ |
| Skills | `SkillsConfigService`, `SkillFrontmatterParser` | Host | **Move** → Skills/ |
| Tools | `CommandRunner`, `ToolProviderAggregator`, `FileToolProvider`, `SearchToolProvider`, `ShellToolProvider`, `TaskToolProvider`, `TimeToolProvider`, `SkillGenerationToolProvider`, `SkillGeneration/` | Host | **Move** → Infrastructure/Agents/Tools/ |

**Host/** (项目根目录)

| 路径 | 组件 | 说明 |
|------|------|------|
| **Sdk/** | `AgentBuilder`, `AgentRunnerAdapter`, `AgentCacheService`, `McpToolFactory`, `TurnContextProvider`, `ChannelStreamSink` | 仅提供基础功能：调用运行 Agent、修改配置、运行子代理等 |
| **Infrastructure/Agents/Tools/** | `ToolProviderAggregator`, `FileToolProvider`, `SearchToolProvider`, `SkillGenerationToolProvider`, ... | 工具提供者实现置于基础设施 |

**Rationale:** Sdk 仅提供基础功能；ToolProviders 实现移至 Infrastructure/Agents/Tools/。

### 2.2 Non-Agents: Host/UI

| Component | Current Location | Target |
|-----------|------------------|--------|
| `CircuitContextHandler` | Services/Circuit | **Keep in Host** — Blazor Circuit |
| `CurrentCircuitAccessor` | Services/Circuit | **Keep in Host** |
| `KeyboardShortcutService` | Services/Presentation | **Keep in Host** |
| `MarkdownService` | Services/Presentation | **Keep in Host** or move to Shared if reusable |

**Rationale:** Circuit and Presentation are UI-bound. No need to move to Application/Infrastructure.

### 2.3 Non-Agents: Shared / Infrastructure

| Component | Current Location | Target |
|-----------|------------------|--------|
| `SkillFrontmatterParser` | Services/Skills | **Infrastructure/Agents** (skills are Agent context) |
| `CommandRunner` | Services/Terminal | **Infrastructure** — new folder `Infrastructure/Terminal` or `Infrastructure/Agents/Tools` |

---

## 3. Migration Phases (Suggested)

### Phase 0: 子领域文件夹创建

1. **Application.Contracts/Agents/** 创建子文件夹：Config/, Mcp/, Skills/, Tools/, Execution/, TurnContext/, Streaming/（Compression/ 已存在）
2. **Application/Agents/** 创建：Compression/, Execution/（TurnContext/ 已存在）
3. **Infrastructure/Agents/** 创建子文件夹：Config/, Mcp/, Skills/, Tools/（Agents 已存在，含 AgentConfigRepository）
4. **Domain/Agents/** 创建：Config/（含 ValueObjects），Services/ 已存在
5. **Host/** 在项目根目录创建 **Sdk/**（仅基础功能）；迁移后删除 Services/Agent 等

### Phase 1: 接口与实现按子领域归位

1. **Contracts** 接口移至对应子文件夹（Config, Mcp, Skills, Tools, Execution, TurnContext, Streaming）
2. **CompressionService** → Application/Agents/Compression/
3. **ConversationAgentDispatcher** → Application/Agents/Execution/
4. **AgentConfigService**, **ModelConfigService**, **TerminalConfigService** → Infrastructure/Agents/Config/
5. **McpConnectionManager**, **McpConfigService**, **McpToolsLoaderService** → Infrastructure/Agents/Mcp/
6. **SkillsConfigService**, **SkillFrontmatterParser** → Infrastructure/Agents/Skills/
7. **CommandRunner**、**ToolProviders**、**SkillGeneration** → Infrastructure/Agents/Tools/
8. **Host** 保留组件：基础功能移至 **Sdk/**

### Phase 2: 接口提取与 DI 更新

1. 确保 `IMcpConnectionManager`、`IToolProvider`、`IToolProviderAggregator`、`ITerminalConfigService` 在 Contracts 对应子领域
2. 更新 `ServiceCollectionExtensions` 的 DI 注册
3. 更新所有 `using` 和 namespace

### Phase 3: Host 瘦身

1. 从 Host 删除已迁移的实现
2. 验证 Host 保留 **Sdk/**（仅基础功能）；ToolProviders 在 Infrastructure/Agents/Tools/

### Phase 4: 清理与验证

1. 删除空文件夹
2. 统一 namespace 命名
3. 验证构建与运行时

---

## 4. Open Questions / User Discussion

### 4.1 Sdk 职责（已定）

**决策：** Sdk 仅提供基础功能：调用运行 Agent、修改配置、运行子代理等。

- 路径：`Host/Sdk/`
- 包含：AgentBuilder, AgentRunnerAdapter, AgentCacheService, McpToolFactory, TurnContextProvider, ChannelStreamSink
- 不含：ToolProviders、SkillGeneration

### 4.2 ToolProviders 与 SkillGeneration 归属（已定）

**决策：** ToolProviders、SkillGeneration 实现置于**基础设施** `Infrastructure/Agents/Tools/`。

### 4.3 ChannelStreamSink

**Question:** ChannelStreamSink is SignalR/Blazor specific. Should it stay in Host?

**Recommendation:** Yes — it implements IStreamSink but the implementation is Blazor-bound. Keep in Host.

### 4.4 Circuit and Presentation

**Question:** Should Circuit and Presentation stay in Host or move to a shared project?

**Recommendation:** Keep in Host — they are UI-bound. No benefit to moving.

### 4.5 New Project: SmallEBot.Agents.Infrastructure?

**Question:** Should we create a dedicated `SmallEBot.Agents.Infrastructure` project for Agent-specific infrastructure?

**Options:**
- A) No — use Infrastructure/Agents folder
- B) Yes — separate project for Agent-specific persistence, MCP, etc.

**Recommendation:** A — current Infrastructure is small enough. Adding `Infrastructure/Agents` subfolder is sufficient.

---

## 5. Risks and Mitigations

| Risk | Mitigation |
|------|-------------|
| Circular DI | Ensure Application → Contracts, Infrastructure → Contracts; Host wires all |
| Breaking changes | Migrate one service at a time, verify build after each |
| Namespace churn | Use `global using` or alias to minimize changes |
| Test coverage | No test project; add manual verification after each phase |

---

## 6. Summary of Recommendations

| Priority | Action |
|----------|--------|
| **High** | Phase 0: 创建 Agents 各层子领域文件夹（Config, Compression, Mcp, Skills, Tools, Execution, TurnContext, Streaming） |
| **High** | Phase 1: 按子领域迁移实现；Contracts 接口归位；CompressionService → Application；Config/Mcp/Skills/Tools 实现 → Infrastructure |
| **High** | 更新 DI 与 namespace |
| **Medium** | Phase 2–3: 接口提取、Host 瘦身 |
| **Low** | Phase 4: 清理、验证 |

---

## 7. Non-Goals (Keep As-Is)

- **AgentBuilder, AgentRunnerAdapter, AgentCacheService, AgentContextFactory, TurnContextProvider, ChannelStreamSink, McpToolFactory** — Application/Agents/（已移至应用服务层，Sdk 已删除）
- **ToolProviders** — Infrastructure/Agents/Tools/
- **SkillGeneration** — Infrastructure/Agents/Tools/SkillGeneration/
- **Circuit, Presentation** — Keep in Host（UI）

---

## 8. Next Steps

1. **User confirmation** on Phase 1 scope and Open Questions (4.1–4.5)
2. **Implement Phase 1** — one service at a time
3. **Verify** build and runtime after each move
4. **Proceed** to Phase 2–4 after Phase 1 complete


---

## 9. Migration Completed

**Date:** 2026-03-08

**Phase 4 Cleanup Summary:**
- Deleted empty folders under SmallEBot/Services/: Agent/, Streaming/, Mcp/, Skills/
- Kept: Circuit/, Presentation/ (UI-bound, per plan)
- Kept: Terminal/ (contains CommandConfirmResult.cs - UI-related)
- Build verified with dotnet build
