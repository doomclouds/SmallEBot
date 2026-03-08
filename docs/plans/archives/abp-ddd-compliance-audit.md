# Workspaces & UserPreferences 领域 ABP vNext DDD 合规性审计

> 对照 ABP vNext 官方 DDD 设计，检查 Workspaces 和 UserPreferences 领域的不合规项。工厂/项目结构保持不变，仅评估领域划分。

## 一、ABP vNext DDD 核心规范摘要

| 规范 | 说明 |
|------|------|
| **Domain 层纯净** | 仅含实体、值对象、聚合根、仓储接口、领域服务；不依赖 Application/Infrastructure |
| **Repository** | 接口在 Domain，实现在 Infrastructure；仅操作聚合根 |
| **Domain Service** | 处理跨实体的业务逻辑，位于 Domain 层 |
| **Aggregate** | 聚合根是修改聚合的唯一入口；聚合应有明确边界 |
| **依赖方向** | Domain ← Application.Contracts ← Application ← Infrastructure |

---

## 二、Workspaces 领域不合规项

### 2.1 Domain 层

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **IVirtualFileSystem 在 Domain** | `Domain/Workspaces/IVirtualFileSystem.cs` 定义文件系统抽象 | 文件 I/O 属于基础设施关注点，应在 Infrastructure 或独立抽象项目 | 将 `IVirtualFileSystem` 移至 `Application.Contracts` 或新建 `*.Abstractions` 项目 |
| **Repository 无对应聚合根** | `IWorkspaceRepository` 返回 `WorkspaceNode`（值对象）、`string` 列表 | Repository 应围绕聚合根设计，如 `IRepository<Workspace, Guid>` | Workspaces 若无聚合根，可考虑将 IWorkspaceRepository 视为「查询仓储」或合并到 IVirtualFileSystem |
| **IWorkspaceRepository 未被使用** | 已注册，但 Application/Host 均未注入 | 死代码，违反 YAGNI | 删除或与 IVirtualFileSystem 职责合并 |
| **WorkspaceReadOnly 为静态类** | `WorkspaceReadOnly` 静态方法承载领域规则 | 领域服务应为实例类，便于扩展和测试 | 改为 `IWorkspaceReadOnlyPolicy` 领域服务或值对象 |
| **IVirtualFileSystem 与 IWorkspaceRepository 职责重叠** | 两者都提供 GetTree、ReadFile 等 | 单一职责，避免重复抽象 | 统一为一种抽象，或明确 VFS=读写、Repository=查询/删除策略 |

### 2.2 Application.Contracts 层

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **IWorkspaceWatcher 在 Contracts** | 文件监听接口在 Application.Contracts | 文件监听属于 Infrastructure 关注点 | 移至 Infrastructure，或通过 `IWorkspaceChangeNotifier` 等应用层事件抽象 |

### 2.3 领域结构

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **无 Workspace 聚合根** | 仅有 ValueObjects（WorkspaceNode, FilePath） | 有界上下文通常有至少一个聚合 | 若 Workspace 本质为「文件系统视图」，可明确为读模型，不强制聚合；或引入 `Workspace` 聚合根 |
| **FilePath 使用 Path.GetExtension** | 值对象依赖 `System.IO.Path` | 值对象应尽量无外部依赖 | 可接受；或封装为领域内路径解析逻辑 |

---

## 三、UserPreferences 领域不合规项

### 3.1 Domain 层

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **UserPreference 无 Id** | 聚合根无标识符 | ABP 聚合根通常有 `Id`（如 Guid） | 单例偏好可接受；若需多租户可加 `UserId` 或固定 `Id` |
| **IUserPreferenceRepository 设计合理** | 接口在 Domain，实现在 Infrastructure | ✓ 符合 | 无需调整 |

### 3.2 Application.Contracts 层

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **IUserPreferencesService 含可变状态** | `CurrentDisplayName` 属性、`UsernameChanged` 事件 | 应用服务应为无状态 | 将 UI 状态移至单独服务（如 `IUserNameDisplayService`）或使用 Blazor 级状态管理 |
| **事件在接口层** | `event Action? UsernameChanged` 在应用服务接口 | 事件通常通过 Domain Events 或消息总线 | 可保留（简化场景）；或改为 `IObservable` / 领域事件 |

### 3.3 Application 层

| 不合规项 | 现状 | ABP 规范 | 建议 |
|----------|------|----------|------|
| **UserPreferencesService 管理 UI 状态** | 在 Get/Set 时更新 CurrentDisplayName 并触发事件 | 应用服务应只编排领域逻辑 | 将 CurrentDisplayName/UsernameChanged 抽离到 Host 层适配器 |

---

## 四、跨领域共性问题

| 不合规项 | 说明 | 建议 |
|----------|------|------|
| **Domain 依赖 Core** | `UserPreference` 依赖 `SmallEBot.Domain.Common.IAggregateRoot` | 可接受，Common 为领域内共享 |
| **AllowedFileExtensions 在 Core** | 扩展名白名单在 Core，Domain/Application 均使用 | 可考虑移至 Domain.Workspaces 作为领域规则 |
| **文件夹命名** | 使用 `Workspaces/`、`UserPreferences/` 复数 | ✓ 与 ABP 有界上下文命名一致 |

---

## 五、合规性总结

| 领域 | 严重不合规 | 轻微不合规 | 符合 |
|------|------------|------------|------|
| **Workspaces** | IVirtualFileSystem 在 Domain、IWorkspaceRepository 死代码、双抽象重叠 | WorkspaceReadOnly 静态、无聚合根 | 值对象、分层结构 |
| **UserPreferences** | 应用服务含 UI 状态 | UserPreference 无 Id、事件在接口 | 聚合根、仓储模式 |

---

## 六、优先修复建议（按影响排序）

1. **删除或合并 IWorkspaceRepository**：当前未被使用，与 IVirtualFileSystem 职责重叠。
2. **将 IVirtualFileSystem 移出 Domain**：迁至 Application.Contracts 或 *.Abstractions。
3. **抽离 IUserPreferencesService 的 UI 状态**：CurrentDisplayName、UsernameChanged 移至 Host 层。
4. **WorkspaceReadOnly 改为领域服务**：`IWorkspaceReadOnlyPolicy` 或值对象。
5. **IWorkspaceWatcher 移至 Infrastructure**：或通过应用层事件抽象。
