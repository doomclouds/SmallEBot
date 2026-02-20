# 配置指令详解

基于文件的应用支持以下配置指令，这些指令必须放在 C# 文件的顶部，使用 `#:` 前缀。

## 指令放置规则

- **所有 `#:` 指令必须放在文件最顶部**
- **指令顺序建议**：
  1. `#:sdk`（如需要）
  2. `#:property`
  3. `#:package`
  4. `#:project`
- **指令后添加空行**，然后才是 `using` 语句

## `#:package` - 添加 NuGet 包引用

```csharp
#:package Newtonsoft.Json
#:package Serilog@3.1.1
#:package Spectre.Console@*
```

### 说明

- 可以指定版本号（如 `@3.1.1`）
- 使用 `@*` 表示使用最新版本
- 如果使用中央包管理（Directory.Packages.props），可以省略版本号

### 示例

```csharp
#:package System.Text.Json
#:package Newtonsoft.Json@13.0.3
#:package CsvHelper
#:package Serilog@3.1.1
```

## `#:project` - 引用其他项目

```csharp
#:project ../SharedLibrary/SharedLibrary.csproj
#:project ./MyProject/MyProject.csproj
```

### 说明

- 可以引用包含项目文件的目录或项目文件本身
- 使用相对路径或绝对路径
- 可以引用多个项目

### 示例

```csharp
#:project ./MyLibrary/MyLibrary.csproj
#:project ../Shared/Shared.csproj
```

## `#:property` - 设置 MSBuild 属性

```csharp
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property LangVersion=latest
```

### 说明

- 用于配置生成属性
- 格式：`属性名=值`
- 常用属性：
  - `TargetFramework`: 目标框架（如 `net10.0`）
  - `PublishAot`: 是否启用本机 AOT（默认 `true`）
  - `LangVersion`: C# 语言版本（如 `latest`、`preview`）

### 示例

```csharp
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property LangVersion=latest
#:property EnableSourceGeneratorSupport=true
```

## `#:sdk` - 指定 SDK 类型

```csharp
#:sdk Microsoft.NET.Sdk.Web
```

### 说明

- 默认为 `Microsoft.NET.Sdk`
- 对于 Web 应用，使用 `Microsoft.NET.Sdk.Web`
- 不同 SDK 会包含不同的默认文件类型

### 示例

```csharp
// Web 应用
#:sdk Microsoft.NET.Sdk.Web
#:property TargetFramework=net10.0

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Hello, World!");
app.Run();
```

## 完整示例

```csharp
// 指令顺序示例
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property LangVersion=latest
#:package Newtonsoft.Json@13.0.3
#:package CsvHelper
#:project ./MyLibrary/MyLibrary.csproj

// 空行后是 using 语句
using System;
using System.IO;
using Newtonsoft.Json;
```

