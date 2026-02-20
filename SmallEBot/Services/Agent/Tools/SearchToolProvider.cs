using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SmallEBot.Core;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides file search tools (GrepFiles, GrepContent).</summary>
public sealed class SearchToolProvider(IVirtualFileSystem vfs) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "Search";
    public bool IsEnabled => true;

    public TimeSpan? GetTimeout(string toolName) => toolName switch
    {
        "GrepFiles" => TimeSpan.FromSeconds(60),
        "GrepContent" => TimeSpan.FromSeconds(60),
        _ => null
    };

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GrepFiles);
        yield return AIFunctionFactory.Create(GrepContent);
    }

    [Description("Find files by name or extension (e.g. all *.cs, *test*.py). Returns JSON with file paths. Use this when the user wants to find/locate FILES by name. pattern: glob like *.cs or regex. mode: 'glob' (default) or 'regex'. path: subdirectory to search (default root). maxDepth: recursion limit (default 10, 0=unlimited).")]
    private string GrepFiles(string pattern, string? mode = null, string? path = null, int maxDepth = 10)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "Error: pattern is required.";

        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var searchDir = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));

        if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        if (!Directory.Exists(searchDir))
            return "Error: directory not found.";

        var effectiveDepth = maxDepth <= 0 ? int.MaxValue : maxDepth;
        var matchMode = (mode ?? "glob").ToLowerInvariant();

        try
        {
            List<string> files;
            if (matchMode == "glob")
            {
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(pattern);
                var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
                files = matchResult.Files
                    .Where(f => effectiveDepth == int.MaxValue || f.Path.Count(c => c == '/') < effectiveDepth)
                    .Select(f =>
                    {
                        var relFromSearch = f.Path.Replace('/', Path.DirectorySeparatorChar);
                        var absolute = Path.GetFullPath(Path.Combine(searchDir, relFromSearch));
                        return Path.GetRelativePath(baseDir, absolute);
                    })
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
                .Select(f => Path.GetRelativePath(baseDir, f))
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
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Find text INSIDE files (search file content). Use when you want to find WHERE something is defined, which files CONTAIN a string, or search within file bodies. pattern: regex matched against each line. path: subdirectory to search (default root). filePattern: restrict to file types (e.g. '*.cs'). ignoreCase: case-insensitive match. filesOnly=true: list matching files only (cheapest; use first to locate files). countOnly=true: match counts per file. contextLines/beforeLines/afterLines: surrounding lines. maxResults: result limit (default 100). maxDepth: directory recursion limit (default 0 = unlimited).")]
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
        int maxResults = 100,
        int maxDepth = 0)
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
        var searchDir = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));

        if (!searchDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        if (!Directory.Exists(searchDir))
            return "Error: directory not found.";

        var preContext = contextLines > 0 ? contextLines : beforeLines;
        var postContext = contextLines > 0 ? contextLines : afterLines;
        const long maxFileSize = 1024 * 1024; // 1MB
        var effectiveDepth = maxDepth <= 0 ? int.MaxValue : maxDepth;

        IEnumerable<string> files;
        if (!string.IsNullOrWhiteSpace(filePattern))
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(filePattern);
            var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
            files = matchResult.Files
                .Where(f => effectiveDepth == int.MaxValue || f.Path.Count(c => c == '/') < effectiveDepth)
                .Select(f => Path.Combine(searchDir, f.Path.Replace('/', Path.DirectorySeparatorChar)))
                .Where(f => AllowedFileExtensions.IsAllowed(Path.GetExtension(f)));
        }
        else
        {
            var enumOptions = effectiveDepth == int.MaxValue
                ? new EnumerationOptions { RecurseSubdirectories = true }
                : new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = effectiveDepth };
            files = Directory.EnumerateFiles(searchDir, "*", enumOptions)
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

                        if (!filesOnly && !countOnly)
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
                    if (filesOnly || countOnly)
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
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }
}
