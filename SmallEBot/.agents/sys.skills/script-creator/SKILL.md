---
name: script-creator
description: Create C# script files. Use this skill when users need to write, create, or generate C# scripts. Supports .NET 10 file-based applications, allowing creation of single-file C# scripts without a full project structure. Suitable for rapid prototyping, automation tasks, data processing, file operations, and similar scenarios. This skill focuses on teaching the Agent how to correctly create C# script files.
---

# C# 脚本创建器

此技能帮助您创建 C# 脚本文件。利用 .NET 10 的基于文件的应用特性，可以创建单文件 C# 脚本，无需创建完整的项目结构。

详细参考文档请查看 [references/](references/) 目录。

## 快速开始

创建一个简单的 C# 脚本：

```csharp
// hello.cs
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Hello, World!");
Console.WriteLine($"当前时间: {DateTime.Now}");
```

## 指令

### 1. 创建 C# 脚本的基本步骤

当用户需要创建 C# 脚本时，按以下步骤操作：

1. **确定脚本功能**：
   - 询问用户脚本的具体用途和需求
   - 了解输入输出要求
   - 确定是否需要外部依赖（NuGet 包、项目引用等）

2. **创建脚本文件**：
   - 使用 `.cs` 扩展名
   - 选择合适的文件名（如 `process-data.cs`、`file-helper.cs`）
   - 放在用户指定的位置（通常是 `scripts/` 目录）

3. **编写脚本结构**：
   - 在文件顶部添加配置指令（如需要）
   - 添加必要的 `using` 语句
   - 编写脚本逻辑（可以使用 Top-level statements）

### 2. 配置指令概述

基于文件的应用支持以下指令，这些指令必须放在 C# 文件的顶部，使用 `#:` 前缀：

- `#:package` - 添加 NuGet 包引用
- `#:project` - 引用其他项目
- `#:property` - 设置 MSBuild 属性
- `#:sdk` - 指定 SDK 类型

**详细说明**：参见 [references/configuration-directives.md](references/configuration-directives.md)

### 3. 脚本文件结构模板

```csharp
// 配置指令（放在文件最顶部）
#:property TargetFramework=net10.0
#:property PublishAot=false
#:package Newtonsoft.Json@13.0.3
#:project ./MyLibrary/MyLibrary.csproj

// using 语句
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

// 设置控制台编码（支持中文输出）
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Top-level statements - 直接编写代码
if (args.Length == 0)
{
    Console.WriteLine("用法: dotnet script.cs <参数>");
    return;
}

// 脚本主要逻辑
var input = args[0];
Console.WriteLine($"处理输入: {input}");
```

**详细说明**：参见 [references/script-structure.md](references/script-structure.md)

### 4. 关键注意事项

1. **指令顺序**：所有 `#:` 指令必须放在文件最顶部，顺序建议：`#:sdk` → `#:property` → `#:package` → `#:project`
2. **编码设置**：在脚本开头设置控制台编码以支持中文
3. **文件位置**：避免放在项目目录结构中，推荐放在独立的 `scripts/` 目录
4. **错误处理**：使用 try-catch 处理异常，使用 `Environment.Exit(1)` 表示失败

**详细说明**：参见 [references/script-structure.md](references/script-structure.md)

## 示例

### 简单脚本

```csharp
// hello.cs
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Hello, World!");
```

### 使用 NuGet 包

```csharp
// json-processor.cs
#:property TargetFramework=net10.0
#:package System.Text.Json

using System.Text.Json;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
// 脚本逻辑...
```

**更多示例**：参见 [references/examples.md](references/examples.md)

## 参考文档

- [配置指令详解](references/configuration-directives.md) - 所有配置指令的详细说明
- [脚本结构模板](references/script-structure.md) - 脚本结构、最佳实践和文件组织
- [示例脚本集合](references/examples.md) - 各种场景的完整示例
- [常见问题](references/faq.md) - FAQ 和注意事项
- [高级用法](references/advanced-usage.md) - Web SDK、异步操作、性能优化等

使用 ReadFile 读取上述文档时，路径为相对运行目录，例如：`.agents/sys.skills/script-creator/references/configuration-directives.md`。

## 要求

- **.NET 10 SDK** 或更高版本
- 基于文件的应用功能需要 .NET 10 SDK

## 参考资源

- [.NET 10 基于文件的应用官方文档](https://learn.microsoft.com/zh-cn/dotnet/core/sdk/file-based-apps)
