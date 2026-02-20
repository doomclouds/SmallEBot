# 常见问题和注意事项

## 配置指令相关

### Q: 指令应该放在哪里？
A: 所有 `#:` 指令必须放在 C# 文件的最顶部，在任何 `using` 语句之前。

### Q: 如何指定 NuGet 包版本？
A: 使用 `@版本号` 格式：
```csharp
#:package Newtonsoft.Json@13.0.3
#:package Serilog@3.1.1
#:package Spectre.Console@*  // 最新版本
```

### Q: 可以引用多个项目吗？
A: 可以，使用多个 `#:project` 指令：
```csharp
#:project ./Project1/Project1.csproj
#:project ./Project2/Project2.csproj
```

### Q: 如何禁用本机 AOT？
A: 使用 `#:property` 指令：
```csharp
#:property PublishAot=false
```

### Q: 如何指定目标框架？
A: 使用 `#:property` 指令：
```csharp
#:property TargetFramework=net10.0
```

## 编码和输出相关

### Q: 如何处理中文编码问题？
A: 在脚本开头添加：
```csharp
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
```

### Q: 文件读写时如何指定编码？
A: 始终明确指定 UTF-8 编码：
```csharp
var content = await File.ReadAllTextAsync("file.txt", Encoding.UTF8);
await File.WriteAllTextAsync("output.txt", content, Encoding.UTF8);
```

## 文件位置和组织

### Q: 脚本应该放在哪里？
A: 
- 避免放在项目目录结构中（可能受项目配置影响）
- 推荐放在独立的 `scripts/` 目录
- 如果需要不同配置，创建隔离目录

### Q: 隐式生成文件会影响脚本吗？
A: 是的，基于文件的应用会遵循父目录中的 MSBuild 配置文件：
- `Directory.Build.props`
- `Directory.Build.targets`
- `Directory.Packages.props`
- `nuget.config`
- `global.json`

如果需要不同的配置，为脚本创建隔离目录。

## 本机 AOT 相关

### Q: 本机 AOT 默认启用吗？
A: 是的，基于文件的应用默认启用本机 AOT 发布。如果需要禁用：
```csharp
#:property PublishAot=false
```

### Q: 为什么需要禁用 AOT？
A: 某些情况下 AOT 可能不兼容：
- 使用反射的代码
- 某些第三方库不支持 AOT
- 需要动态加载的程序集

## 错误处理

### Q: 如何正确退出脚本？
A: 使用 `Environment.Exit(1)` 表示失败：
```csharp
if (error)
{
    Console.WriteLine("错误消息");
    Environment.Exit(1);
}
```

### Q: 如何验证文件是否存在？
A: 使用 `File.Exists()` 或 `Directory.Exists()`：
```csharp
if (!File.Exists(inputFile))
{
    Console.WriteLine($"错误: 文件不存在: {inputFile}");
    Environment.Exit(1);
}
```

## 依赖和包管理

### Q: 如何使用中央包管理？
A: 如果项目使用 `Directory.Packages.props`，可以省略包版本：
```csharp
#:package Newtonsoft.Json  // 版本由 Directory.Packages.props 管理
```

### Q: 如何引用本地项目？
A: 使用相对路径或绝对路径：
```csharp
#:project ./MyLibrary/MyLibrary.csproj
#:project ../Shared/Shared.csproj
```

## 要求和环境

### Q: 需要什么版本的 .NET SDK？
A: 需要 .NET 10 SDK 或更高版本。使用 `dotnet --version` 检查版本。

### Q: 脚本需要项目文件吗？
A: 不需要。基于文件的应用可以直接运行 `.cs` 文件，SDK 会自动生成临时项目配置。

