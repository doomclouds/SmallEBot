# 高级用法

## 使用 Web SDK

创建 Web 应用脚本：

```csharp
// web-app.cs
#:sdk Microsoft.NET.Sdk.Web
#:property TargetFramework=net10.0

using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello, World!");
app.MapGet("/api/hello", () => new { Message = "Hello from API" });

app.Run();
```

## 使用中央包管理

如果项目使用 `Directory.Packages.props`，可以省略包版本：

```csharp
#:package Newtonsoft.Json  // 版本由 Directory.Packages.props 管理
```

## 异步操作

### 并行处理文件

```csharp
var files = Directory.GetFiles(".", "*.txt").ToList();
var tasks = files.Select(async file =>
{
    var content = await File.ReadAllTextAsync(file, Encoding.UTF8);
    // 处理内容
    return processed;
});

var results = await Task.WhenAll(tasks);
```

### 流式处理大文件

```csharp
using var reader = new StreamReader(inputFile, Encoding.UTF8);
using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);

string? line;
while ((line = await reader.ReadLineAsync()) != null)
{
    var processed = ProcessLine(line);
    await writer.WriteLineAsync(processed);
}
```

## 使用 LINQ

```csharp
using System.Linq;

var files = Directory.GetFiles(".", "*.txt")
    .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddDays(-7))
    .OrderBy(f => File.GetLastWriteTime(f))
    .ToList();
```

## 使用正则表达式

```csharp
using System.Text.RegularExpressions;

var pattern = @"\d{4}-\d{2}-\d{2}";
var matches = Regex.Matches(text, pattern);
foreach (Match match in matches)
{
    Console.WriteLine(match.Value);
}
```

## 使用 JSON

### System.Text.Json

```csharp
#:package System.Text.Json

using System.Text.Json;
using System.Text;

var json = await File.ReadAllTextAsync("data.json", Encoding.UTF8);
var doc = JsonDocument.Parse(json);

// 访问 JSON 数据
var root = doc.RootElement;
var value = root.GetProperty("key").GetString();
```

### Newtonsoft.Json

```csharp
#:package Newtonsoft.Json@13.0.3

using Newtonsoft.Json;
using System.Text;

var json = await File.ReadAllTextAsync("data.json", Encoding.UTF8);
var obj = JsonConvert.DeserializeObject<MyClass>(json);

// 序列化
var output = JsonConvert.SerializeObject(obj, Formatting.Indented);
await File.WriteAllTextAsync("output.json", output, Encoding.UTF8);
```

## 使用 CSV

```csharp
#:package CsvHelper

using CsvHelper;
using System.Globalization;
using System.Text;

using var reader = new StreamReader("data.csv", Encoding.UTF8);
using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

var records = csv.GetRecords<MyRecord>().ToList();
```

## 命令行参数解析

### 简单解析

```csharp
var argsDict = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 < args.Length)
    {
        argsDict[args[i].TrimStart('-')] = args[i + 1];
    }
}

var input = argsDict.GetValueOrDefault("input", "default.txt");
```

### 使用 System.CommandLine（需要 NuGet 包）

```csharp
#:package System.CommandLine

using System.CommandLine;

var inputOption = new Option<string>("--input", "输入文件");
var outputOption = new Option<string>("--output", "输出文件");

var rootCommand = new RootCommand
{
    inputOption,
    outputOption
};

rootCommand.SetHandler((input, output) =>
{
    // 处理逻辑
}, inputOption, outputOption);

await rootCommand.InvokeAsync(args);
```

## 日志记录

### 使用 Serilog

```csharp
#:package Serilog

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("脚本开始执行");
Log.Error("发生错误: {Error}", ex.Message);
```

## 环境变量和配置

```csharp
// 读取环境变量
var apiKey = Environment.GetEnvironmentVariable("API_KEY");

// 读取配置（需要 NuGet 包）
#:package Microsoft.Extensions.Configuration

using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var setting = config["MySetting"];
```

## 多文件脚本组织

对于复杂脚本，可以将逻辑分离到多个文件中，但需要转换为项目结构：

```csharp
// 主脚本
#:project ./Helpers/Helpers.csproj

using Helpers;

var result = Helper.Process(args[0]);
```

## 性能优化

### 大文件处理

```csharp
// 使用流式处理，避免一次性加载到内存
using var reader = new StreamReader(inputFile, Encoding.UTF8);
using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);

const int bufferSize = 8192;
var buffer = new char[bufferSize];
int charsRead;

while ((charsRead = await reader.ReadAsync(buffer, 0, bufferSize)) > 0)
{
    // 处理缓冲区
    await writer.WriteAsync(buffer, 0, charsRead);
}
```

### 并行处理

```csharp
var files = Directory.GetFiles(".", "*.txt");
var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

Parallel.ForEach(files, parallelOptions, file =>
{
    // 处理文件
});
```

