# Grep Tools Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add two built-in tools (GrepFiles, GrepContent) for searching files and content in the workspace.

**Architecture:** Implement both tools in `BuiltInToolFactory.cs` using .NET built-in regex and `Microsoft.Extensions.FileSystemGlobbing` for glob patterns. Both tools respect `AllowedFileExtensions` and workspace boundaries.

**Tech Stack:** .NET 10, System.Text.RegularExpressions, Microsoft.Extensions.FileSystemGlobbing

---

## Task 1: Add GrepFiles Tool

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Add using statement for globbing**

Add at line 1 (after existing usings):

```csharp
using Microsoft.Extensions.FileSystemGlobbing;
```

**Step 2: Add GrepFiles method**

Add before `CreateTools()` method (around line 142):

```csharp
[Description("Search for files by name pattern in the workspace. Returns JSON with matching file paths. pattern: search pattern. mode: 'glob' (default, e.g. '*.cs', '**/test*.py') or 'regex'. path: optional starting directory (default: workspace root). maxDepth: max recursion depth (default: 10, 0 for unlimited).")]
private string GrepFiles(string pattern, string? mode = null, string? path = null, int maxDepth = 10)
{
    if (string.IsNullOrWhiteSpace(pattern))
        return "Error: pattern is required.";

    var baseDir = Path.GetFullPath(vfs.GetRootPath());
    var searchDir = string.IsNullOrWhiteSpace(path) || path!.Trim() == "."
        ? baseDir
        : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));

    if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        return "Error: path must be under the workspace.";
    if (!Directory.Exists(searchDir))
        return "Error: directory not found.";

    var effectiveDepth = maxDepth <= 0 ? int.MaxValue : maxDepth;
    var files = new List<string>();
    var matchMode = (mode ?? "glob").ToLowerInvariant();

    try
    {
        if (matchMode == "glob")
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
            files = result.Files
                .Select(f => f.Path.Replace('/', Path.DirectorySeparatorChar))
                .Where(f => AllowedFileExtensions.IsAllowed(Path.GetExtension(f)))
                .ToList();
        }
        else if (matchMode == "regex")
        {
            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                return $"Error: Invalid regex pattern: {ex.Message}";
            }

            files = Directory.EnumerateFiles(searchDir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = effectiveDepth
            })
            .Where(f => AllowedFileExtensions.IsAllowed(Path.GetExtension(f)))
            .Where(f =>
            {
                var relativePath = Path.GetRelativePath(searchDir, f);
                return regex.IsMatch(relativePath);
            })
            .Select(f => string.IsNullOrEmpty(path) || path!.Trim() == "."
                ? Path.GetRelativePath(baseDir, f)
                : Path.GetRelativePath(searchDir, f))
            .ToList();
        }
        else
        {
            return "Error: mode must be 'glob' or 'regex'.";
        }

        const int maxFiles = 500;
        var truncated = files.Count > maxFiles;
        var limitedFiles = files.Take(maxFiles).ToList();

        return JsonSerializer.Serialize(new
        {
            pattern,
            mode = matchMode,
            path = path ?? ".",
            files = limitedFiles,
            count = files.Count,
            truncated
        }, TaskFileJsonOptions);
    }
    catch (Exception ex)
    {
        return "Error: " + ex.Message;
    }
}
```

**Step 3: Register GrepFiles in CreateTools**

Modify `CreateTools()` method (around line 156), add before `AIFunctionFactory.Create(ExecuteCommand)`:

```csharp
        AIFunctionFactory.Create(GrepFiles),
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(agent): add GrepFiles tool for workspace file search"
```

---

## Task 2: Add GrepContent Tool

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Add GrepContent method**

Add after `GrepFiles` method (before `CreateTools()`):

```csharp
[Description("Search file content in the workspace with regex. Returns JSON with matches. pattern: regex pattern. path: optional starting directory (default: workspace root). filePattern: glob filter (e.g. '*.cs'), default all allowed extensions. ignoreCase: case-insensitive (default false). contextLines: show N lines before and after match (default 0). beforeLines: show N lines before match (default 0). afterLines: show N lines after match (default 0). filesOnly: return only file names (default false). countOnly: return only match counts (default false). invertMatch: show non-matching lines (default false). maxResults: max matches (default 100).")]
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
    if (string.IsNullOrWhiteSpace(pattern))
        return "Error: pattern is required.";

    Regex regex;
    try
    {
        var options = RegexOptions.Compiled;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        regex = new Regex(pattern, options);
    }
    catch (ArgumentException ex)
    {
        return $"Error: Invalid regex pattern: {ex.Message}";
    }

    var baseDir = Path.GetFullPath(vfs.GetRootPath());
    var searchDir = string.IsNullOrWhiteSpace(path) || path!.Trim() == "."
        ? baseDir
        : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));

    if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        return "Error: path must be under the workspace.";
    if (!Directory.Exists(searchDir))
        return "Error: directory not found.";

    var preContext = contextLines > 0 ? contextLines : beforeLines;
    var postContext = contextLines > 0 ? contextLines : afterLines;
    const long maxFileSize = 1024 * 1024; // 1MB

    IEnumerable<string> files;
    if (!string.IsNullOrWhiteSpace(filePattern))
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(filePattern);
        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
        files = result.Files
            .Select(f => Path.Combine(searchDir, f.Path.Replace('/', Path.DirectorySeparatorChar)))
            .Where(f => AllowedFileExtensions.IsAllowed(Path.GetExtension(f)));
    }
    else
    {
        files = Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories)
            .Where(f => AllowedFileExtensions.IsAllowed(Path.GetExtension(f)));
    }

    var results = new List<object>();
    var totalMatches = 0;
    var filesScanned = 0;
    var truncated = false;

    try
    {
        foreach (var filePath in files)
        {
            if (truncated) break;

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > maxFileSize)
                continue;

            filesScanned++;
            var lines = File.ReadAllLines(filePath);
            var fileMatches = new List<object>();
            var fileMatchCount = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                if (totalMatches >= maxResults)
                {
                    truncated = true;
                    break;
                }

                var line = lines[i];
                var isMatch = regex.IsMatch(line);

                if (invertMatch)
                    isMatch = !isMatch;

                if (isMatch)
                {
                    fileMatchCount++;
                    totalMatches++;

                    if (filesOnly)
                    {
                        // Only need file name, skip content
                    }
                    else if (countOnly)
                    {
                        // Only need count, skip content
                    }
                    else
                    {
                        var context = new List<string>();
                        if (preContext > 0 || postContext > 0)
                        {
                            var contextStart = Math.Max(0, i - preContext);
                            var contextEnd = Math.Min(lines.Length - 1, i + postContext);
                            for (var j = contextStart; j <= contextEnd; j++)
                            {
                                context.Add($"{j + 1}: {lines[j]}");
                            }
                        }

                        fileMatches.Add(new
                        {
                            line = i + 1,
                            content = line,
                            context = context.Count > 0 ? context : null
                        });
                    }
                }
            }

            if (fileMatchCount > 0)
            {
                var relativePath = Path.GetRelativePath(baseDir, filePath);
                if (filesOnly)
                {
                    results.Add(new { file = relativePath, matchCount = fileMatchCount });
                }
                else if (countOnly)
                {
                    results.Add(new { file = relativePath, matchCount = fileMatchCount });
                }
                else
                {
                    results.Add(new
                    {
                        file = relativePath,
                        matches = fileMatches,
                        matchCount = fileMatchCount
                    });
                }
            }
        }

        return JsonSerializer.Serialize(new
        {
            pattern,
            path = path ?? ".",
            filePattern,
            results,
            totalMatches,
            filesScanned,
            truncated
        }, TaskFileJsonOptions);
    }
    catch (Exception ex)
    {
        return "Error: " + ex.Message;
    }
}
```

**Step 2: Register GrepContent in CreateTools**

Modify `CreateTools()` method, add after `AIFunctionFactory.Create(GrepFiles)`:

```csharp
        AIFunctionFactory.Create(GrepContent),
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(agent): add GrepContent tool for workspace content search"
```

---

## Task 3: Update AGENTS.md Documentation

**Files:**
- Modify: `AGENTS.md`

**Step 1: Add GrepFiles to Built-in tools table**

In the Built-in tools table (around line 75-88), add after `ListFiles`:

```markdown
|| `GrepFiles(pattern, mode?, path?, maxDepth?)` | Search file names by glob (default) or regex pattern; returns JSON with file paths relative to workspace root ||
|| `GrepContent(pattern, ...)` | Search file content with regex; supports ignoreCase, contextLines, filesOnly, countOnly, invertMatch, filePattern, maxResults; returns JSON with matches ||
```

**Step 2: Commit**

```bash
git add AGENTS.md
git commit -m "docs: add GrepFiles and GrepContent to built-in tools table"
```

---

## Task 4: Update README.md Documentation

**Files:**
- Modify: `README.md`

**Step 1: Add tools to 内置工具 table**

In the 内置工具 table (around line 200-210), add after `ListFiles`:

```markdown
| `GrepFiles(pattern, ...)` | 按模式搜索文件名 |
| `GrepContent(pattern, ...)` | 搜索文件内容（支持正则） |
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add GrepFiles and GrepContent to README"
```

---

## Verification

**Final verification steps:**

1. Build: `dotnet build` - should succeed
2. Run: `dotnet run --project SmallEBot` - should start without errors
3. Test tools via chat:
   - Ask agent to use `GrepFiles("*.cs")` - should list .cs files
   - Ask agent to use `GrepContent("TODO", "*.cs")` - should search for TODO in .cs files

---

**Plan complete and saved to `docs/plans/2026-02-19-grep-tools-implementation-plan.md`.**

**Two execution options:**

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Direct Implementation (me)** - I implement directly in this session with step-by-step verification

**Which approach?**
