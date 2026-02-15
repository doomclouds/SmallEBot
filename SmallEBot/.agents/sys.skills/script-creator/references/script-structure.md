# 脚本结构模板和最佳实践

## 基本脚本结构

创建脚本时，使用以下结构：

```csharp
// 1. 配置指令（放在文件最顶部）
#:property TargetFramework=net10.0
#:property PublishAot=false
#:package Newtonsoft.Json@13.0.3
#:project ./MyLibrary/MyLibrary.csproj

// 2. using 语句
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

// 3. 设置控制台编码（支持中文输出）
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// 4. Top-level statements - 直接编写代码
if (args.Length == 0)
{
    Console.WriteLine("用法: dotnet script.cs <参数>");
    return;
}

// 5. 脚本主要逻辑
var input = args[0];
Console.WriteLine($"处理输入: {input}");

// 6. 可以定义函数和类
static void ProcessData(string data)
{
    // 处理逻辑
}
```

## 编码设置

始终在脚本开头设置控制台编码以支持中文：

```csharp
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
```

## 参数处理

### 基本参数验证

```csharp
if (args.Length == 0)
{
    Console.WriteLine("用法: dotnet script.cs <input-file>");
    return;
}

var inputFile = args[0];
var outputFile = args.Length > 1 ? args[1] : "output.txt";
```

### 参数解析示例

```csharp
if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet script.cs <input> <output>");
    Environment.Exit(1);
}

var input = args[0];
var output = args[1];
```

## 错误处理

### 基本错误处理

```csharp
try
{
    // 脚本逻辑
    var result = ProcessData();
    Console.WriteLine($"结果: {result}");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"错误: 文件未找到 - {ex.FileName}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
    Environment.Exit(1);
}
```

### 文件操作错误处理

```csharp
if (!File.Exists(inputFile))
{
    Console.WriteLine($"错误: 文件不存在: {inputFile}");
    Environment.Exit(1);
}

try
{
    var content = await File.ReadAllTextAsync(inputFile, Encoding.UTF8);
    // 处理内容
}
catch (IOException ex)
{
    Console.WriteLine($"IO 错误: {ex.Message}");
    Environment.Exit(1);
}
```

## 文件操作

### 读取文件

```csharp
using System.IO;
using System.Text;

// 读取文本文件
var content = await File.ReadAllTextAsync("input.txt", Encoding.UTF8);

// 读取所有行
var lines = await File.ReadAllLinesAsync("input.txt", Encoding.UTF8);

// 逐行读取
await foreach (var line in File.ReadLinesAsync("input.txt", Encoding.UTF8))
{
    // 处理每一行
}
```

### 写入文件

```csharp
// 写入文本文件
await File.WriteAllTextAsync("output.txt", content, Encoding.UTF8);

// 写入所有行
await File.WriteAllLinesAsync("output.txt", lines, Encoding.UTF8);

// 追加内容
await File.AppendAllTextAsync("output.txt", content, Encoding.UTF8);
```

### 目录操作

```csharp
// 检查目录是否存在
if (!Directory.Exists(inputDir))
{
    Console.WriteLine($"错误: 目录不存在: {inputDir}");
    return;
}

// 创建目录
Directory.CreateDirectory(outputDir);

// 获取文件列表
foreach (var file in Directory.GetFiles(inputDir, "*.txt"))
{
    // 处理文件
}
```

## 代码组织

### 简单脚本（Top-level statements）

```csharp
// 简单脚本直接使用 Top-level statements
Console.WriteLine("Hello, World!");
```

### 复杂脚本（使用函数）

```csharp
// 定义函数处理复杂逻辑
static async Task ProcessFileAsync(string filePath)
{
    var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
    // 处理逻辑
    return processed;
}

// 主逻辑
var result = await ProcessFileAsync(args[0]);
Console.WriteLine($"结果: {result}");
```

### 使用类组织代码

```csharp
class FileProcessor
{
    public static async Task<string> ProcessAsync(string input)
    {
        // 处理逻辑
        return processed;
    }
}

var result = await FileProcessor.ProcessAsync(args[0]);
```

## 文件命名规范

- 使用描述性的文件名（如 `process-data.cs` 而非 `script.cs`）
- 使用小写字母和连字符
- 避免使用空格和特殊字符
- 文件名应该反映脚本的功能

## 文件位置建议

### ❌ 不建议

```
📁 MyProject/
├── MyProject.csproj
├── Program.cs
└──📁 scripts/
    └── utility.cs  // 可能受项目配置影响
```

### ✅ 推荐

```
📁 MyProject/
├── MyProject.csproj
└── Program.cs
📁 scripts/
└── utility.cs  // 独立目录，不受项目配置影响
```

## 隐式生成文件的影响

基于文件的应用会遵循父目录中的 MSBuild 配置文件：
- `Directory.Build.props` - 影响所有子项目
- `Directory.Build.targets` - 自定义生成逻辑
- `Directory.Packages.props` - 中央包管理
- `nuget.config` - NuGet 配置
- `global.json` - SDK 版本

如果需要不同的配置，为脚本创建隔离目录。

