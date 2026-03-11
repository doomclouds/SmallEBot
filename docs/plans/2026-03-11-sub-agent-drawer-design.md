# Sub-Agent Drawer 设计

**Date:** 2026-03-11  
**Status:** Implemented  
**Supersedes:** 2026-03-11-sub-agent-design.md（UI 部分）

## 目标

简化子代理展示：不在主聊天中显示子代理流，改为通过 AppBar 状态入口打开抽屉，仅展示**执行中**的子代理。

## 需求摘要

| 项目 | 说明 |
|------|------|
| 主聊天 | 不再显示子代理块；RunSubAgent 仍以普通 ToolCall 显示（工具名、参数、结果） |
| 入口 | AppBar 新增可点击状态区，有运行中子代理时高亮 |
| 抽屉 | 上下分栏，最多 2 个槽位，仅显示执行中的子代理 |
| 渲染 | 与 MessageThread 相同（thinking、tool calls、text） |
| 存储 | 执行中：内存缓存；完成后：落盘并清除缓存；抽屉只显示缓存内容 |

## 架构

### 数据流

```
SubAgentOrchestrator 写入 IAmbientStreamSink (SubAgentStreamUpdate)
    ↓
ChannelStreamSink 写入 Channel（与主 agent 共用）
    ↓
ChatOrchestrator 读取：
  - SubAgentStreamUpdate → 不加入 _streamingUpdates，转发到 ISubAgentLiveCache
  - 其他 → 照旧加入 _streamingUpdates
    ↓
SubAgentOrchestrator 完成时 → ISubAgentLiveCache.Complete() 移除
    ↓
SubAgentDrawer 订阅缓存变更，展示运行中的子代理
```

### 组件

| 组件 | 位置 | 职责 |
|------|------|------|
| `ISubAgentLiveCache` | Application.Contracts | 运行中子代理的内存缓存；AddUpdate、Complete、GetRunning、OnChanged |
| `SubAgentLiveCache` | Infrastructure 或 Host | 实现；Singleton |
| `SubAgentDrawer` | SmallEBot/Components | 抽屉 UI；上下 2 槽位；复用 ChatPresentationService 渲染 |
| AppBar 状态入口 | MainLayout | 图标/徽章；点击打开 SubAgentDrawer |

### ISubAgentLiveCache 接口

```csharp
public interface ISubAgentLiveCache
{
    void AddUpdate(Guid conversationId, Guid subAgentId, string subAgentName, StreamUpdate update);
    void Complete(Guid conversationId, Guid subAgentId);
    IReadOnlyList<SubAgentLiveEntry> GetRunning(Guid conversationId);
    event Action? OnChanged;
}

public record SubAgentLiveEntry(Guid SubAgentId, string SubAgentName, IReadOnlyList<StreamUpdate> Updates);
```

### 抽屉布局

- 与 WorkspaceDrawer、TaskListDrawer 同风格
- 上下两槽：Slot 1（上）、Slot 2（下）
- 1 个运行中：只显示 Slot 1
- 2 个运行中：两槽都显示
- 每个槽：子代理名 + MessageThread 风格内容（ConvertStreamToBubbleBlocks）

### 存储策略

- **执行中**：`SubAgentLiveCache` 按 `(ConversationId, SubAgentId)` 累积 `StreamUpdate`
- **完成后**：`SubAgentRunner` 的 `finally` 已调用 `SubAgentSessionStore.SaveAsync` 落盘；`SubAgentOrchestrator` 的 `finally` 调用 `ISubAgentLiveCache.Complete` 清除缓存
- **抽屉**：仅读取 `GetRunning(ConversationId)`，故只显示执行中的内容

## 修改清单

| 文件 | 操作 |
|------|------|
| Application.Contracts | 新增 `ISubAgentLiveCache`、`SubAgentLiveEntry` |
| Infrastructure 或 Host | 实现 `SubAgentLiveCache`（Singleton） |
| Application/SubAgentOrchestrator | 注入 `ISubAgentLiveCache`，`finally` 中调用 `Complete` |
| ChatOrchestrator | 注入 `ISubAgentLiveCache`；收到 `SubAgentStreamUpdate` 时转发到 cache，不加入 `_streamingUpdates` |
| ChatPresentationService | 移除 `AppendSubAgentUpdate` 及子代理相关逻辑 |
| IBubbleBlock / MessageThread | 移除 `SubAgentBlockModel`、`SubAgentBlock` |
| MainLayout | 新增子代理状态入口，`SubAgentDrawer` |
| 新建 SubAgentDrawer.razor | 抽屉组件 |

## 待确认

1. 状态入口样式：与 Workspace/TaskList 相同的图标按钮，还是单独的「状态栏」区域？
2. 抽屉位置：与 Workspace/TaskList 同侧（右侧）？
3. RunSubAgent 在聊天中的展示：是否仍显示为普通 ToolCall（含参数和结果）？设计假定：是。
