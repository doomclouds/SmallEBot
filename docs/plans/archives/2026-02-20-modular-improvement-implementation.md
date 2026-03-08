# SmallEBot Modular Improvement Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement modular improvements for SmallEBot covering context management, tool system refactoring, and interaction optimization.

**Architecture:** Layered approach - Core models in SmallEBot.Core, service interfaces in Application, implementations in Host. Each phase builds on previous, with P1 establishing foundations for P2 and P3.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, EF Core (SQLite), Microsoft.Extensions.AI, SignalR

---

## Prerequisites

Before starting implementation:

1. Ensure solution builds: `dotnet build`
2. Run the app to verify baseline: `dotnet run --project SmallEBot`
3. Note: No test project exists; verification is manual or via runtime checks

---

## Phase 1: Core Capabilities

### Task 1.1: Create ToolResult Model

**Files:**
- Create: `SmallEBot.Core/Models/ToolResult.cs`

**Step 1: Create the ToolResult record**

```csharp
namespace SmallEBot.Core.Models;

/// <summary>Structured result from tool execution.</summary>
public record ToolResult(
    bool Success,
    string? Output,
    ToolError? Error,
    TimeSpan Elapsed)
{
    public static ToolResult Ok(string output, TimeSpan elapsed) =>
        new(true, output, null, elapsed);

    public static ToolResult Fail(string code, string message, bool retryable, TimeSpan elapsed) =>
        new(false, null, new ToolError(code, message, retryable), elapsed);
}

/// <summary>Structured error information.</summary>
public record ToolError(
    string Code,
    string Message,
    bool Retryable);
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Core`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot.Core/Models/ToolResult.cs
git commit -m "feat(core): add ToolResult model for structured tool responses"
```

---

### Task 1.2: Create IContextWindowManager Interface

**Files:**
- Create: `SmallEBot.Application/Context/IContextWindowManager.cs`

**Step 1: Create the interface**

```csharp
using SmallEBot.Core.Entities;

namespace SmallEBot.Application.Context;

/// <summary>Manages context window for conversations.</summary>
public interface IContextWindowManager
{
    /// <summary>Estimate total tokens for messages.</summary>
    int EstimateTokens(IReadOnlyList<ChatMessage> messages);

    /// <summary>Trim messages to fit within token limit.</summary>
    TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens);
}

/// <summary>Result of trimming messages.</summary>
public record TrimResult(
    IReadOnlyList<ChatMessage> Messages,
    int TotalTokens,
    int TrimmedCount);
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot.Application/Context/IContextWindowManager.cs
git commit -m "feat(app): add IContextWindowManager interface"
```

---

### Task 1.3: Implement ContextWindowManager

**Files:**
- Create: `SmallEBot/Services/Context/ContextWindowManager.cs`

**Step 1: Create the implementation**

```csharp
using SmallEBot.Application.Context;
using SmallEBot.Core.Entities;
using SmallEBot.Services.Agent;

namespace SmallEBot.Services.Context;

/// <summary>Manages context window using tokenizer for estimation.</summary>
public sealed class ContextWindowManager(ITokenizer tokenizer) : IContextWindowManager
{
    public int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return 0;
        var total = 0;
        foreach (var msg in messages)
        {
            total += tokenizer.CountTokens(msg.Content);
            total += 4; // role overhead estimate
        }
        return total;
    }

    public TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens)
    {
        if (messages.Count == 0)
            return new TrimResult([], 0, 0);

        var tokens = EstimateTokens(messages);
        if (tokens <= maxTokens)
            return new TrimResult(messages, tokens, 0);

        // Keep newest messages, trim oldest
        var result = new List<ChatMessage>();
        var currentTokens = 0;
        var trimmed = 0;

        // Iterate from newest to oldest
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var msgTokens = tokenizer.CountTokens(msg.Content) + 4;
            if (currentTokens + msgTokens <= maxTokens)
            {
                result.Insert(0, msg);
                currentTokens += msgTokens;
            }
            else
            {
                trimmed++;
            }
        }

        return new TrimResult(result, currentTokens, trimmed);
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Context/ContextWindowManager.cs
git commit -m "feat(host): implement ContextWindowManager with sliding window trim"
```

---

### Task 1.4: Register ContextWindowManager in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add using statement**

Add at top of file:
```csharp
using SmallEBot.Application.Context;
using SmallEBot.Services.Context;
```

**Step 2: Register the service**

Find the agent services registration section and add:
```csharp
services.AddSingleton<IContextWindowManager, ContextWindowManager>();
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register IContextWindowManager"
```

---

### Task 1.5: Integrate ContextWindowManager in AgentRunnerAdapter

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add constructor parameter**

Update constructor to include:
```csharp
public sealed class AgentRunnerAdapter(
    IConversationRepository conversationRepository,
    IAgentBuilder agentBuilder,
    ITurnContextFragmentBuilder fragmentBuilder,
    IContextWindowManager contextWindowManager) : IAgentRunner
```

**Step 2: Add trimming logic before agent call**

In `RunStreamingAsync`, after building `frameworkMessages` and before calling agent:

```csharp
// Trim to fit context window (reserve 20% for response)
var maxInputTokens = (int)(agentBuilder.GetContextWindowTokens() * 0.8);
var coreMessages = history.ToList();
var trimResult = contextWindowManager.TrimToFit(coreMessages, maxInputTokens);
if (trimResult.TrimmedCount > 0)
{
    frameworkMessages = trimResult.Messages
        .Select(m => new ChatMessage(ToChatRole(m.Role), m.Content))
        .ToList();
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Manual test**

Run: `dotnet run --project SmallEBot`
Expected: App starts, conversations work normally

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): integrate context window trimming in AgentRunnerAdapter"
```

---

### Task 1.6: Add Async Command Execution Interface

**Files:**
- Modify: `SmallEBot/Services/Terminal/ICommandRunner.cs`

**Step 1: Add async streaming method to interface**

```csharp
namespace SmallEBot.Services.Terminal;

/// <summary>Runs a shell command on the host.</summary>
public interface ICommandRunner
{
    /// <summary>Runs the command synchronously.</summary>
    string Run(string command, string workingDirectory, TimeSpan? timeout = null, IReadOnlyDictionary<string, string>? environmentOverrides = null);

    /// <summary>Runs the command asynchronously with streaming output.</summary>
    IAsyncEnumerable<CommandOutput> RunStreamingAsync(
        string command,
        string workingDirectory,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        CancellationToken ct = default);
}

/// <summary>Output from command execution.</summary>
public record CommandOutput(OutputType Type, string Content);

/// <summary>Type of command output.</summary>
public enum OutputType { Stdout, Stderr, ExitCode }
```

**Step 2: Build (will fail - implementation needed)**

Run: `dotnet build SmallEBot`
Expected: Build fails (CommandRunner doesn't implement new method)

**Step 3: Commit interface change**

```powershell
git add SmallEBot/Services/Terminal/ICommandRunner.cs
git commit -m "feat(terminal): add async streaming interface to ICommandRunner"
```

---

### Task 1.7: Implement Async Command Execution

**Files:**
- Modify: `SmallEBot/Services/Terminal/CommandRunner.cs`

**Step 1: Read current implementation**

First understand the current `Run` method implementation.

**Step 2: Add async streaming implementation**

Add this method to `CommandRunner` class:

```csharp
public async IAsyncEnumerable<CommandOutput> RunStreamingAsync(
    string command,
    string workingDirectory,
    TimeSpan? timeout = null,
    IReadOnlyDictionary<string, string>? environmentOverrides = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var effectiveTimeout = timeout ?? _config.GetTimeout();
    var isWindows = OperatingSystem.IsWindows();
    var shell = isWindows ? "cmd.exe" : "/bin/sh";
    var shellArg = isWindows ? "/c" : "-c";

    var psi = new ProcessStartInfo
    {
        FileName = shell,
        Arguments = $"{shellArg} \"{command.Replace("\"", "\\\"")}\"",
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    if (environmentOverrides != null)
    {
        foreach (var (key, value) in environmentOverrides)
            psi.Environment[key] = value;
    }

    using var process = new Process { StartInfo = psi };
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(effectiveTimeout);

    process.Start();

    var stdoutTask = ReadStreamAsync(process.StandardOutput, OutputType.Stdout, cts.Token);
    var stderrTask = ReadStreamAsync(process.StandardError, OutputType.Stderr, cts.Token);

    await foreach (var output in MergeStreamsAsync(stdoutTask, stderrTask, cts.Token))
    {
        yield return output;
    }

    try
    {
        await process.WaitForExitAsync(cts.Token);
        yield return new CommandOutput(OutputType.ExitCode, process.ExitCode.ToString());
    }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        yield return new CommandOutput(OutputType.ExitCode, "-1");
    }
}

private static async IAsyncEnumerable<CommandOutput> ReadStreamAsync(
    StreamReader reader,
    OutputType type,
    [EnumeratorCancellation] CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(ct);
        if (line == null) break;
        yield return new CommandOutput(type, line);
    }
}

private static async IAsyncEnumerable<CommandOutput> MergeStreamsAsync(
    IAsyncEnumerable<CommandOutput> stream1,
    IAsyncEnumerable<CommandOutput> stream2,
    [EnumeratorCancellation] CancellationToken ct)
{
    var channel = System.Threading.Channels.Channel.CreateUnbounded<CommandOutput>();
    
    async Task ReadToChannel(IAsyncEnumerable<CommandOutput> source)
    {
        await foreach (var item in source.WithCancellation(ct))
            await channel.Writer.WriteAsync(item, ct);
    }

    var t1 = ReadToChannel(stream1);
    var t2 = ReadToChannel(stream2);
    _ = Task.WhenAll(t1, t2).ContinueWith(_ => channel.Writer.Complete(), ct);

    await foreach (var item in channel.Reader.ReadAllAsync(ct))
        yield return item;
}
```

**Step 3: Add required using**

Add at top:
```csharp
using System.Diagnostics;
using System.Runtime.CompilerServices;
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Terminal/CommandRunner.cs
git commit -m "feat(terminal): implement async streaming command execution"
```

---

### Task 1.8: Add Concurrent Task List Cache

**Files:**
- Create: `SmallEBot/Services/Conversation/TaskListCache.cs`

**Step 1: Create the cache service**

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Conversation;

/// <summary>In-memory cache for task lists with write-back to file.</summary>
public interface ITaskListCache
{
    TaskListData GetOrLoad(Guid conversationId);
    void Update(Guid conversationId, TaskListData data);
    void Remove(Guid conversationId);
}

public sealed class TaskListCache : ITaskListCache, IDisposable
{
    private readonly ConcurrentDictionary<Guid, TaskListData> _cache = new();
    private readonly ConcurrentDictionary<Guid, bool> _dirty = new();
    private readonly Timer _flushTimer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TaskListCache()
    {
        _flushTimer = new Timer(_ => FlushDirty(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public TaskListData GetOrLoad(Guid conversationId)
    {
        return _cache.GetOrAdd(conversationId, id =>
        {
            var path = GetPath(id);
            if (!File.Exists(path)) return new TaskListData([]);
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<TaskListData>(json, JsonOptions) ?? new TaskListData([]);
            }
            catch
            {
                return new TaskListData([]);
            }
        });
    }

    public void Update(Guid conversationId, TaskListData data)
    {
        _cache[conversationId] = data;
        _dirty[conversationId] = true;
    }

    public void Remove(Guid conversationId)
    {
        _cache.TryRemove(conversationId, out _);
        _dirty.TryRemove(conversationId, out _);
        var path = GetPath(conversationId);
        if (File.Exists(path)) File.Delete(path);
    }

    private void FlushDirty()
    {
        foreach (var id in _dirty.Keys.ToList())
        {
            if (_dirty.TryRemove(id, out _) && _cache.TryGetValue(id, out var data))
            {
                var path = GetPath(id);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(path, json);
            }
        }
    }

    private static string GetPath(Guid id) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "tasks", $"{id:N}.json");

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushDirty();
    }
}

public record TaskListData(List<TaskItem> Tasks);

public record TaskItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
    
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Conversation/TaskListCache.cs
git commit -m "feat(tasks): add concurrent task list cache with write-back"
```

---

## Phase 2: Tool System

### Task 2.1: Create IToolProvider Interface

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/IToolProvider.cs`

**Step 1: Create the interface**

```csharp
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides a set of AI tools.</summary>
public interface IToolProvider
{
    /// <summary>Provider name for identification.</summary>
    string Name { get; }

    /// <summary>Whether this provider is currently enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Get all tools from this provider.</summary>
    IEnumerable<AITool> GetTools();
}
```

**Step 2: Create the aggregator interface**

Create file `SmallEBot/Services/Agent/Tools/IToolProviderAggregator.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Aggregates tools from all registered providers.</summary>
public interface IToolProviderAggregator
{
    /// <summary>Get all tools from all enabled providers.</summary>
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/
git commit -m "feat(tools): add IToolProvider and IToolProviderAggregator interfaces"
```

---

### Task 2.2: Extract TimeToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/TimeToolProvider.cs`

**Step 1: Create the provider**

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides time-related tools.</summary>
public sealed class TimeToolProvider : IToolProvider
{
    public string Name => "Time";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GetCurrentTime);
    }

    [Description("Returns the current local date and time on the host machine.")]
    private static string GetCurrentTime() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss (ddd)");
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/TimeToolProvider.cs
git commit -m "feat(tools): extract TimeToolProvider from BuiltInToolFactory"
```

---

### Task 2.3: Extract FileToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/FileToolProvider.cs`

**Step 1: Create the provider**

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;
using SmallEBot.Core;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides file operation tools (ReadFile, WriteFile, ListFiles).</summary>
public sealed class FileToolProvider(IVirtualFileSystem vfs) : IToolProvider
{
    public string Name => "File";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ReadFile);
        yield return AIFunctionFactory.Create(WriteFile);
        yield return AIFunctionFactory.Create(ListFiles);
    }

    [Description("Read file content from workspace. path: relative path within workspace.")]
    private string ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: path is required.";

        var root = Path.GetFullPath(vfs.GetRootPath());
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "Error: Path is outside workspace.";
        if (!File.Exists(full))
            return $"Error: File not found: {path}";

        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (!AllowedFileExtensions.Extensions.Contains(ext))
            return $"Error: Reading {ext} files is not allowed.";

        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [Description("Write content to a file in workspace. path: relative path. content: file content.")]
    private string WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: path is required.";

        var root = Path.GetFullPath(vfs.GetRootPath());
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "Error: Path is outside workspace.";

        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (!AllowedFileExtensions.Extensions.Contains(ext))
            return $"Error: Writing {ext} files is not allowed.";

        try
        {
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(full, content ?? "");
            return $"OK: Wrote {(content ?? "").Length} chars to {path}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [Description("List files and directories in workspace. path: optional relative path (default: root).")]
    private string ListFiles(string? path = null)
    {
        var root = Path.GetFullPath(vfs.GetRootPath());
        var target = string.IsNullOrWhiteSpace(path)
            ? root
            : Path.GetFullPath(Path.Combine(root, path));

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "Error: Path is outside workspace.";
        if (!Directory.Exists(target))
            return $"Error: Directory not found: {path ?? "/"}";

        try
        {
            var entries = new List<string>();
            foreach (var dir in Directory.GetDirectories(target))
                entries.Add(Path.GetFileName(dir) + "/");
            foreach (var file in Directory.GetFiles(target))
                entries.Add(Path.GetFileName(file));
            return string.Join("\n", entries);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/FileToolProvider.cs
git commit -m "feat(tools): extract FileToolProvider from BuiltInToolFactory"
```

---

### Task 2.4: Extract SearchToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/SearchToolProvider.cs`

**Step 1: Create the provider**

Copy the `GrepFiles` and `GrepContent` methods from `BuiltInToolFactory` to a new provider class:

```csharp
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides file search tools (GrepFiles, GrepContent).</summary>
public sealed class SearchToolProvider(IVirtualFileSystem vfs) : IToolProvider
{
    public string Name => "Search";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GrepFiles);
        yield return AIFunctionFactory.Create(GrepContent);
    }

    [Description("Search for files by name pattern in the workspace. Returns JSON with matching file paths.")]
    private string GrepFiles(string pattern, string? mode = null, string? path = null, int maxDepth = 10)
    {
        // Implementation copied from BuiltInToolFactory.GrepFiles
        // (Full implementation to be copied during actual execution)
        if (string.IsNullOrWhiteSpace(pattern))
            return "Error: pattern is required.";

        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var searchDir = string.IsNullOrWhiteSpace(path)
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path));

        if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: Path is outside workspace.";
        if (!Directory.Exists(searchDir))
            return $"Error: Directory not found: {path ?? "/"}";

        var useRegex = string.Equals(mode, "regex", StringComparison.OrdinalIgnoreCase);
        var matches = new List<string>();

        try
        {
            if (useRegex)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                SearchRegex(searchDir, baseDir, regex, matches, 0, maxDepth);
            }
            else
            {
                var matcher = new Matcher();
                matcher.AddInclude(pattern);
                var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
                matches.AddRange(result.Files.Select(f => f.Path.Replace('\\', '/')));
            }

            return JsonSerializer.Serialize(new { files = matches }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static void SearchRegex(string dir, string baseDir, Regex regex, List<string> matches, int depth, int maxDepth)
    {
        if (maxDepth > 0 && depth >= maxDepth) return;
        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (regex.IsMatch(name))
                matches.Add(Path.GetRelativePath(baseDir, file).Replace('\\', '/'));
        }
        foreach (var subDir in Directory.GetDirectories(dir))
            SearchRegex(subDir, baseDir, regex, matches, depth + 1, maxDepth);
    }

    [Description("Search file content with regex pattern. Returns JSON with matches.")]
    private string GrepContent(
        string pattern,
        string? path = null,
        string? filePattern = null,
        bool ignoreCase = false,
        int contextLines = 0,
        int beforeLines = 0,
        int afterLines = 0,
        bool filesOnly = false,
        bool countOnly = false,
        bool invertMatch = false,
        int maxResults = 100)
    {
        // Implementation copied from BuiltInToolFactory.GrepContent
        // (Full implementation to be copied during actual execution)
        if (string.IsNullOrWhiteSpace(pattern))
            return "Error: pattern is required.";

        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var searchDir = string.IsNullOrWhiteSpace(path)
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path));

        if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: Path is outside workspace.";

        // Simplified placeholder - full implementation to copy from BuiltInToolFactory
        return JsonSerializer.Serialize(new { matches = Array.Empty<object>() });
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/SearchToolProvider.cs
git commit -m "feat(tools): extract SearchToolProvider from BuiltInToolFactory"
```

---

### Task 2.5: Extract ShellToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs`

**Step 1: Create the provider**

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides shell command execution tool.</summary>
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandConfirmationService confirmationService,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
{
    public string Name => "Shell";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ExecuteCommand);
    }

    [Description("Execute a shell command. command: the command to run. workingDirectory: optional working directory (default: workspace root).")]
    private async Task<string> ExecuteCommand(string command, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";

        var root = Path.GetFullPath(vfs.GetRootPath());
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? root
            : Path.GetFullPath(Path.Combine(root, workingDirectory));

        if (!cwd.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "Error: Working directory is outside workspace.";

        var cfg = await terminalConfig.GetConfigAsync(cancellationToken);
        if (cfg.BlackList.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Error: Command contains blacklisted terms.";

        if (cfg.RequireConfirmation)
        {
            var whitelisted = cfg.WhiteList.Any(w => command.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            if (!whitelisted)
            {
                var confirmResult = await confirmationService.RequestConfirmationAsync(command, cwd, cancellationToken);
                if (!confirmResult.Approved)
                    return $"Command rejected by user: {confirmResult.Reason ?? "No reason given"}";
            }
        }

        return commandRunner.Run(command, cwd);
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded (may need to add using statements)

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/ShellToolProvider.cs
git commit -m "feat(tools): extract ShellToolProvider from BuiltInToolFactory"
```

---

### Task 2.6: Extract TaskToolProvider

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/TaskToolProvider.cs`

**Step 1: Create the provider**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides task list management tools.</summary>
public sealed class TaskToolProvider(
    IConversationTaskContext taskContext,
    ITaskListCache taskCache) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "Task";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ListTasks);
        yield return AIFunctionFactory.Create(SetTaskList);
        yield return AIFunctionFactory.Create(CompleteTask);
        yield return AIFunctionFactory.Create(ClearTasks);
    }

    [Description("List tasks for the current conversation.")]
    private string ListTasks()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: No conversation context.";

        var data = taskCache.GetOrLoad(conversationId.Value);
        return JsonSerializer.Serialize(new { tasks = data.Tasks }, JsonOptions);
    }

    [Description("Create or replace the task list. Pass JSON array of {title, description?}.")]
    private string SetTaskList(string tasksJson)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: No conversation context.";

        List<TaskInputItem>? input;
        try
        {
            input = JsonSerializer.Deserialize<List<TaskInputItem>>(tasksJson, JsonOptions);
        }
        catch
        {
            return "Error: Invalid JSON.";
        }

        if (input == null || input.Count == 0)
            return "Error: Empty task list.";

        var tasks = input
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => new TaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = t.Title!.Trim(),
                Description = t.Description ?? "",
                Done = false
            })
            .ToList();

        if (tasks.Count == 0)
            return "Error: No valid tasks.";

        taskCache.Update(conversationId.Value, new TaskListData(tasks));
        return JsonSerializer.Serialize(new { tasks }, JsonOptions);
    }

    [Description("Mark a task as done by id.")]
    private string CompleteTask(string taskId)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: No conversation context.";

        var data = taskCache.GetOrLoad(conversationId.Value);
        var task = data.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
            return JsonSerializer.Serialize(new { ok = false, error = "Task not found" });

        task.Done = true;
        taskCache.Update(conversationId.Value, data);
        return JsonSerializer.Serialize(new { ok = true, task });
    }

    [Description("Clear all tasks for the current conversation.")]
    private string ClearTasks()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: No conversation context.";

        taskCache.Remove(conversationId.Value);
        return JsonSerializer.Serialize(new { ok = true });
    }

    private record TaskInputItem(string? Title, string? Description);
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/TaskToolProvider.cs
git commit -m "feat(tools): extract TaskToolProvider from BuiltInToolFactory"
```

---

### Task 2.7: Create ToolProviderAggregator

**Files:**
- Create: `SmallEBot/Services/Agent/Tools/ToolProviderAggregator.cs`

**Step 1: Create the aggregator**

```csharp
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Aggregates all registered tool providers.</summary>
public sealed class ToolProviderAggregator(IEnumerable<IToolProvider> providers) : IToolProviderAggregator
{
    public Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default)
    {
        var tools = providers
            .Where(p => p.IsEnabled)
            .SelectMany(p => p.GetTools())
            .ToArray();
        return Task.FromResult(tools);
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/ToolProviderAggregator.cs
git commit -m "feat(tools): add ToolProviderAggregator implementation"
```

---

### Task 2.8: Register Tool Providers in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add using statement**

```csharp
using SmallEBot.Services.Agent.Tools;
```

**Step 2: Register providers**

Add in the agent services section:

```csharp
// Tool providers
services.AddSingleton<IToolProvider, TimeToolProvider>();
services.AddSingleton<IToolProvider, FileToolProvider>();
services.AddSingleton<IToolProvider, SearchToolProvider>();
services.AddSingleton<IToolProvider, ShellToolProvider>();
services.AddSingleton<IToolProvider, TaskToolProvider>();
services.AddSingleton<IToolProviderAggregator, ToolProviderAggregator>();

// Task list cache
services.AddSingleton<ITaskListCache, TaskListCache>();
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register tool providers and aggregator"
```

---

### Task 2.9: Update AgentBuilder to Use Aggregator

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentBuilder.cs`

**Step 1: Update constructor**

Replace `IBuiltInToolFactory` with `IToolProviderAggregator`:

```csharp
public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpToolFactory mcpToolFactory,
    IConfiguration config,
    ILogger<AgentBuilder> log) : IAgentBuilder
```

**Step 2: Update tool loading**

In `GetOrCreateAgentAsync`, replace:

```csharp
if (_allTools == null)
{
    var builtIn = await toolAggregator.GetAllToolsAsync(ct);
    var (mcpTools, clients) = await mcpToolFactory.LoadAsync(ct);
    _mcpClients = clients.Count > 0 ? [.. clients] : null;
    var combined = new List<AITool>(builtIn.Length + mcpTools.Length);
    combined.AddRange(builtIn);
    combined.AddRange(mcpTools);
    _allTools = combined.ToArray();
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Manual test**

Run: `dotnet run --project SmallEBot`
Expected: App starts, tools work normally

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Agent/AgentBuilder.cs
git commit -m "refactor(agent): use IToolProviderAggregator instead of IBuiltInToolFactory"
```

---

## Phase 3: Interaction Optimization

### Task 3.1: Create IWorkspaceWatcher Interface

**Files:**
- Create: `SmallEBot/Services/Workspace/IWorkspaceWatcher.cs`

**Step 1: Create the interface**

```csharp
namespace SmallEBot.Services.Workspace;

/// <summary>Watches workspace for file system changes.</summary>
public interface IWorkspaceWatcher : IDisposable
{
    /// <summary>Fired when a file or directory changes.</summary>
    event Action<WorkspaceChangeEvent>? OnChange;

    /// <summary>Start watching.</summary>
    void Start();

    /// <summary>Stop watching.</summary>
    void Stop();
}

/// <summary>Workspace change event.</summary>
public record WorkspaceChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Workspace/IWorkspaceWatcher.cs
git commit -m "feat(workspace): add IWorkspaceWatcher interface"
```

---

### Task 3.2: Implement WorkspaceWatcher

**Files:**
- Create: `SmallEBot/Services/Workspace/WorkspaceWatcher.cs`

**Step 1: Create the implementation**

```csharp
using System.Collections.Concurrent;

namespace SmallEBot.Services.Workspace;

/// <summary>FileSystemWatcher-based workspace watcher with debouncing.</summary>
public sealed class WorkspaceWatcher : IWorkspaceWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new();
    private readonly Timer _debounceTimer;
    private const int DebounceMs = 500;

    public event Action<WorkspaceChangeEvent>? OnChange;

    public WorkspaceWatcher(IVirtualFileSystem vfs)
    {
        var root = vfs.GetRootPath();
        Directory.CreateDirectory(root);

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Created);
        _watcher.Changed += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Changed);
        _watcher.Deleted += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Deleted);
        _watcher.Renamed += (_, e) =>
        {
            QueueChange(e.OldFullPath, WatcherChangeTypes.Deleted);
            QueueChange(e.FullPath, WatcherChangeTypes.Created);
        };

        _debounceTimer = new Timer(FlushPending, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void QueueChange(string fullPath, WatcherChangeTypes type)
    {
        var relative = Path.GetRelativePath(_watcher.Path, fullPath).Replace('\\', '/');
        _pending[relative] = DateTime.UtcNow;
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void FlushPending(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-DebounceMs);
        foreach (var kvp in _pending.ToArray())
        {
            if (kvp.Value <= cutoff && _pending.TryRemove(kvp.Key, out _))
            {
                OnChange?.Invoke(new WorkspaceChangeEvent(WatcherChangeTypes.Changed, kvp.Key));
            }
        }

        if (!_pending.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Workspace/WorkspaceWatcher.cs
git commit -m "feat(workspace): implement WorkspaceWatcher with debouncing"
```

---

### Task 3.3: Update WorkspaceDrawer to Use Watcher

**Files:**
- Modify: `SmallEBot/Components/Workspace/WorkspaceDrawer.razor`

**Step 1: Inject the watcher**

Add at top:
```razor
@inject IWorkspaceWatcher WorkspaceWatcher
```

**Step 2: Replace polling with event subscription**

Replace `OnParametersSetAsync`, `StartPolling`, `StopPolling` with:

```csharp
protected override async Task OnParametersSetAsync()
{
    if (Open)
    {
        await LoadTreeAsync();
        SubscribeToChanges();
    }
    else
    {
        UnsubscribeFromChanges();
    }
}

private void SubscribeToChanges()
{
    WorkspaceWatcher.OnChange += HandleChange;
    WorkspaceWatcher.Start();
}

private void UnsubscribeFromChanges()
{
    WorkspaceWatcher.OnChange -= HandleChange;
}

private async void HandleChange(WorkspaceChangeEvent e)
{
    await InvokeAsync(async () =>
    {
        await LoadTreeAsync();
        if (!string.IsNullOrEmpty(_selectedPath) && e.RelativePath == _selectedPath)
        {
            _viewerContent = await WorkspaceService.ReadFileContentAsync(_selectedPath);
        }
        StateHasChanged();
    });
}

public void Dispose()
{
    UnsubscribeFromChanges();
}
```

**Step 3: Remove polling constants and fields**

Remove:
```csharp
private const int PollIntervalMs = 2000;
private CancellationTokenSource? _pollCts;
```

**Step 4: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```powershell
git add SmallEBot/Components/Workspace/WorkspaceDrawer.razor
git commit -m "refactor(workspace): replace polling with FileSystemWatcher events"
```

---

### Task 3.4: Add Message Edit Support - Data Model

**Files:**
- Modify: `SmallEBot.Core/Entities/ChatMessage.cs`

**Step 1: Add edit tracking fields**

```csharp
public class ChatMessage : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // New fields for edit support
    public Guid? ReplacedByMessageId { get; set; }
    public bool IsEdited { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Core`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot.Core/Entities/ChatMessage.cs
git commit -m "feat(core): add edit tracking fields to ChatMessage"
```

---

### Task 3.5: Add EF Core Migration for Message Edit

**Files:**
- Migration files (auto-generated)

**Step 1: Create migration**

Run: `dotnet ef migrations add AddMessageEditSupport --project SmallEBot.Infrastructure --startup-project SmallEBot`
Expected: Migration created successfully

**Step 2: Verify migration**

Check that migration file includes:
- `ReplacedByMessageId` column (nullable Guid)
- `IsEdited` column (bool)

**Step 3: Commit**

```powershell
git add SmallEBot.Infrastructure/Migrations/
git commit -m "feat(db): add migration for message edit support"
```

---

### Task 3.6: Add Keyboard Shortcuts - JS Interop

**Files:**
- Create: `SmallEBot/wwwroot/js/keyboard-shortcuts.js`

**Step 1: Create the JS file**

```javascript
window.keyboardShortcuts = {
    callbacks: {},

    register: function (dotNetRef, shortcuts) {
        this.callbacks = {};
        shortcuts.forEach(s => {
            this.callbacks[s.key.toLowerCase()] = {
                ctrl: s.ctrl || false,
                shift: s.shift || false,
                alt: s.alt || false,
                method: s.method
            };
        });

        this.handler = (e) => {
            const key = e.key.toLowerCase();
            const callback = this.callbacks[key];
            if (!callback) return;

            if (callback.ctrl !== e.ctrlKey) return;
            if (callback.shift !== e.shiftKey) return;
            if (callback.alt !== e.altKey) return;

            e.preventDefault();
            dotNetRef.invokeMethodAsync(callback.method);
        };

        document.addEventListener('keydown', this.handler);
    },

    unregister: function () {
        if (this.handler) {
            document.removeEventListener('keydown', this.handler);
            this.handler = null;
        }
        this.callbacks = {};
    }
};
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/wwwroot/js/keyboard-shortcuts.js
git commit -m "feat(ui): add keyboard shortcuts JS interop"
```

---

### Task 3.7: Add Keyboard Shortcuts Service

**Files:**
- Create: `SmallEBot/Services/Presentation/KeyboardShortcutService.cs`

**Step 1: Create the service**

```csharp
using Microsoft.JSInterop;

namespace SmallEBot.Services.Presentation;

/// <summary>Manages keyboard shortcuts via JS interop.</summary>
public sealed class KeyboardShortcutService(IJSRuntime js) : IAsyncDisposable
{
    private DotNetObjectReference<KeyboardShortcutService>? _dotNetRef;

    public event Func<Task>? OnSend;
    public event Func<Task>? OnCancel;
    public event Func<Task>? OnNewConversation;
    public event Func<Task>? OnFocusSearch;
    public event Func<Task>? OnToggleToolCalls;
    public event Func<Task>? OnToggleReasoning;

    public async Task RegisterAsync()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        var shortcuts = new[]
        {
            new { key = "Enter", ctrl = true, shift = false, alt = false, method = "InvokeSend" },
            new { key = "Escape", ctrl = false, shift = false, alt = false, method = "InvokeCancel" },
            new { key = "n", ctrl = true, shift = false, alt = false, method = "InvokeNewConversation" },
            new { key = "/", ctrl = true, shift = false, alt = false, method = "InvokeFocusSearch" },
            new { key = "t", ctrl = true, shift = true, alt = false, method = "InvokeToggleToolCalls" },
            new { key = "r", ctrl = true, shift = true, alt = false, method = "InvokeToggleReasoning" }
        };
        await js.InvokeVoidAsync("keyboardShortcuts.register", _dotNetRef, shortcuts);
    }

    [JSInvokable] public async Task InvokeSend() => await (OnSend?.Invoke() ?? Task.CompletedTask);
    [JSInvokable] public async Task InvokeCancel() => await (OnCancel?.Invoke() ?? Task.CompletedTask);
    [JSInvokable] public async Task InvokeNewConversation() => await (OnNewConversation?.Invoke() ?? Task.CompletedTask);
    [JSInvokable] public async Task InvokeFocusSearch() => await (OnFocusSearch?.Invoke() ?? Task.CompletedTask);
    [JSInvokable] public async Task InvokeToggleToolCalls() => await (OnToggleToolCalls?.Invoke() ?? Task.CompletedTask);
    [JSInvokable] public async Task InvokeToggleReasoning() => await (OnToggleReasoning?.Invoke() ?? Task.CompletedTask);

    public async ValueTask DisposeAsync()
    {
        await js.InvokeVoidAsync("keyboardShortcuts.unregister");
        _dotNetRef?.Dispose();
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Presentation/KeyboardShortcutService.cs
git commit -m "feat(ui): add KeyboardShortcutService for shortcut handling"
```

---

### Task 3.8: Add Conversation Search to Repository

**Files:**
- Modify: `SmallEBot.Core/Repositories/IConversationRepository.cs`
- Modify: `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`

**Step 1: Add interface method**

In `IConversationRepository.cs`, add:
```csharp
Task<List<Conversation>> SearchAsync(
    string userName,
    string query,
    bool includeContent = false,
    CancellationToken ct = default);
```

**Step 2: Implement in repository**

In `ConversationRepository.cs`, add:
```csharp
public async Task<List<Conversation>> SearchAsync(
    string userName,
    string query,
    bool includeContent = false,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(query))
        return await GetAllForUserAsync(userName, ct);

    var queryLower = query.ToLowerInvariant();

    var conversations = await _db.Conversations
        .Where(c => c.UserName == userName)
        .OrderByDescending(c => c.UpdatedAt)
        .ToListAsync(ct);

    return conversations
        .Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot.Core/Repositories/IConversationRepository.cs
git add SmallEBot.Infrastructure/Repositories/ConversationRepository.cs
git commit -m "feat(repo): add conversation search by title"
```

---

## Finalization

### Task F.1: Remove Deprecated BuiltInToolFactory

**Files:**
- Delete or deprecate: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Mark as obsolete or delete**

If other code still references it, mark with `[Obsolete]`. Otherwise, delete the file.

**Step 2: Update DI registration**

Remove `IBuiltInToolFactory` registration from `ServiceCollectionExtensions.cs` if present.

**Step 3: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add -A
git commit -m "refactor: remove deprecated BuiltInToolFactory"
```

---

### Task F.2: Full Integration Test

**Step 1: Build solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors

**Step 2: Run application**

Run: `dotnet run --project SmallEBot`
Expected: Application starts without errors

**Step 3: Manual verification checklist**

- [ ] New conversation works
- [ ] File tools (ReadFile, WriteFile, ListFiles) work
- [ ] Search tools (GrepFiles, GrepContent) work
- [ ] Shell command execution works
- [ ] Task list tools work
- [ ] Workspace drawer updates on file changes
- [ ] Keyboard shortcuts work (Ctrl+Enter, Escape, Ctrl+N)

**Step 4: Final commit**

```powershell
git add -A
git commit -m "feat: complete modular improvement implementation"
```

---

## Summary

| Phase | Tasks | Commits |
|-------|-------|---------|
| P1: Core Capabilities | 8 | 8 |
| P2: Tool System | 9 | 9 |
| P3: Interaction Optimization | 8 | 8 |
| Finalization | 2 | 2 |
| **Total** | **27** | **27** |

Estimated implementation time depends on complexity of existing code integration and testing thoroughness.
