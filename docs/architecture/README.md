---
title: SmallEBot 架构文档
version: 1.0.0
lastUpdated: 2026-03-01
powerBy: Claude Code
---

# SmallEBot 架构文档

## 项目概述

**SmallEBot** 是一个基于 **.NET 10** 的本地 AI 助手应用程序，使用 **Blazor Server** 和 **MudBlazor** 构建用户界面。它通过 **Microsoft.Agents.AI** 框架与大语言模型 API（DeepSeek 或其他 Anthropic 兼容端点）进行交互。

---

## 文档导航

| 文档 | 描述 |
|----------|-------------|
| [01-系统架构.md](./01-系统架构.md) | 整体系统架构、分层设计与部署模型 |
| [02-项目结构.md](./02-项目结构.md) | 解决方案结构、项目依赖与文件组织 |
| [03-核心层.md](./03-核心层.md) | 领域实体、模型与仓储接口 |
| [04-应用层.md](./04-应用层.md) | 业务逻辑、对话管道与流式接口 |
| [05-基础设施层.md](./05-基础设施层.md) | 数据库、EF Core、迁移与仓储实现 |
| [06-宿主服务层.md](./06-宿主服务层.md) | 宿主层服务：Agent、MCP、工作区、终端等 |
| [07-聊天UI架构.md](./07-聊天UI架构.md) | Blazor UI 架构、状态管理与组件设计 |
| [08-工具系统.md](./08-工具系统.md) | AI 工具提供者、MCP 集成与技能系统 |
| [09-时序图.md](./09-时序图.md) | 关键流程时序图 |
| [10-类图.md](./10-类图.md) | 核心组件 UML 类图 |

---

## 快速了解

### 技术栈

| 层级 | 技术 |
|-------|------------|
| 运行时 | .NET 10 |
| UI 框架 | Blazor Server + MudBlazor |
| AI Agent | Microsoft.Agents.AI + Anthropic SDK |
| LLM API | DeepSeek / Anthropic 兼容接口 |
| 数据库 | EF Core + SQLite |
| MCP | ModelContextProtocol |

### 核心概念

1. **分层架构**: Core → Application → Infrastructure → Host
2. **对话管道**: 请求 → 服务 → 运行器 → Agent → LLM
3. **流式输出**: 通过 `IStreamSink` 实现实时 UI 更新
4. **工具系统**: 内置工具 + MCP 工具 + 技能工具
5. **上下文压缩**: Token 使用量超阈值时自动摘要

---

## 架构亮点

### 请求流程

```
用户输入 → Blazor UI → SignalR → ChatArea
    ↓
ConversationService → IAgentConversationService
    ↓
CreateTurn + StreamResponse → IAgentRunner
    ↓
AgentRunnerAdapter → AIAgent → LLM API
    ↓
IStreamSink → ChannelStreamSink → UI 更新
```

### 关键设计模式

- **状态容器模式**: `ChatState` 通过事件管理 UI 状态
- **提供者模式**: `IToolProvider` 实现可扩展工具系统
- **适配器模式**: `AgentRunnerAdapter` 桥接应用层和 Agent 层
- **仓储模式**: `IConversationRepository` 抽象数据访问
- **工厂模式**: `IAgentContextFactory` 创建系统提示词

---

## 项目统计

| 指标 | 数值 |
|--------|-------|
| 项目数 | 4 个 (Core, Application, Infrastructure, Host) |
| 源文件 | ~120 个 .cs 文件 |
| UI 组件 | ~50 个 .razor 文件 |
| 数据表 | 5 个 (Conversations, ChatMessages, ToolCalls, ThinkBlocks, ConversationTurns) |

---

## 相关文档

- [CLAUDE.md](../../CLAUDE.md) - 开发指南和命令
- [docs/plans/](../plans/) - 设计文档和实现说明
