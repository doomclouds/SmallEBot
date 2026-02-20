# 示例脚本集合

## 示例 1：简单脚本

```csharp
// hello.cs
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Hello, World!");
Console.WriteLine($"当前时间: {DateTime.Now}");
```

## 示例 2：文件处理脚本

```csharp
// process-files.cs
#:property TargetFramework=net10.0
#:property PublishAot=false

using System.IO;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet process-files.cs <input-dir> <output-dir>");
    return;
}

var inputDir = args[0];
var outputDir = args[1];

if (!Directory.Exists(inputDir))
{
    Console.WriteLine($"错误: 输入目录不存在: {inputDir}");
    return;
}

Directory.CreateDirectory(outputDir);

foreach (var file in Directory.GetFiles(inputDir, "*.txt"))
{
    var content = await File.ReadAllTextAsync(file, Encoding.UTF8);
    var processed = content.ToUpper();
    var outputFile = Path.Combine(outputDir, Path.GetFileName(file));
    await File.WriteAllTextAsync(outputFile, processed, Encoding.UTF8);
    Console.WriteLine($"处理完成: {file} -> {outputFile}");
}
```

## 示例 3：JSON 处理脚本

```csharp
// json-processor.cs
#:property TargetFramework=net10.0
#:package System.Text.Json

using System.Text.Json;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    Console.WriteLine("用法: dotnet json-processor.cs <json-file>");
    return;
}

var jsonFile = args[0];
var json = await File.ReadAllTextAsync(jsonFile, Encoding.UTF8);
var doc = JsonDocument.Parse(json);

Console.WriteLine("JSON 内容:");
Console.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
```

## 示例 4：数据转换脚本（使用 NuGet 包）

```csharp
// csv-to-json.cs
#:property TargetFramework=net10.0
#:package CsvHelper
#:package Newtonsoft.Json@13.0.3

using CsvHelper;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet csv-to-json.cs <input.csv> <output.json>");
    return;
}

var csvFile = args[0];
var jsonFile = args[1];

try
{
    using var reader = new StreamReader(csvFile, Encoding.UTF8);
    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
    var records = csv.GetRecords<dynamic>().ToList();

    var json = JsonConvert.SerializeObject(records, Formatting.Indented);
    await File.WriteAllTextAsync(jsonFile, json, Encoding.UTF8);

    Console.WriteLine($"转换完成: {csvFile} -> {jsonFile}");
    Console.WriteLine($"记录数: {records.Count}");
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
    Environment.Exit(1);
}
```

## 示例 5：引用项目的脚本

```csharp
// use-library.cs
#:property TargetFramework=net10.0
#:project ./MyLibrary/MyLibrary.csproj

using System.Text;
using MyLibrary;

Console.OutputEncoding = Encoding.UTF8;

var service = new MyService();
var result = service.DoSomething();
Console.WriteLine($"结果: {result}");
```

## 示例 6：Web 应用脚本

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

## 示例 7：完整脚本模板

```csharp
// script-template.cs
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property LangVersion=latest
#:package Newtonsoft.Json@13.0.3

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// 脚本逻辑
if (args.Length == 0)
{
    Console.WriteLine("用法: dotnet script-template.cs <参数>");
    Environment.Exit(1);
}

try
{
    // 主要逻辑
    var result = ProcessData(args[0]);
    Console.WriteLine($"结果: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
    Environment.Exit(1);
}

static string ProcessData(string input)
{
    // 处理逻辑
    return input.ToUpper();
}
```

## 示例 8：异步文件处理

```csharp
// async-processor.cs
#:property TargetFramework=net10.0

using System.IO;
using System.Text;
using System.Linq;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet async-processor.cs <input-dir> <output-dir>");
    return;
}

var inputDir = args[0];
var outputDir = args[1];

var files = Directory.GetFiles(inputDir, "*.txt").ToList();
var tasks = files.Select(async file =>
{
    var content = await File.ReadAllTextAsync(file, Encoding.UTF8);
    var processed = content.ToUpper();
    var outputFile = Path.Combine(outputDir, Path.GetFileName(file));
    await File.WriteAllTextAsync(outputFile, processed, Encoding.UTF8);
    return file;
});

var results = await Task.WhenAll(tasks);
Console.WriteLine($"处理完成 {results.Length} 个文件");
```

