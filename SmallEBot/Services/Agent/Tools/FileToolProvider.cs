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

    private const int LargeFileLineThreshold = 200;

    public TimeSpan? GetTimeout(string toolName) => toolName switch
    {
        "WriteFile" => TimeSpan.FromMinutes(10),
        "ReadFile" => TimeSpan.FromSeconds(30),
        "ListFiles" => TimeSpan.FromSeconds(30),
        _ => null
    };

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ReadFile);
        yield return AIFunctionFactory.Create(WriteFile);
        yield return AIFunctionFactory.Create(AppendFile);
        yield return AIFunctionFactory.Create(ListFiles);
    }

    [Description("Read a text file from the workspace. path: relative to workspace root (e.g. notes.txt or src/script.py). startLine/endLine: optional 1-based line numbers to read only part of a large file (e.g. startLine=10, endLine=50). lineNumbers: if true, prefix each output line with its 1-based number — useful when cross-referencing GrepContent results. When the response header says [Total: N lines] and N is large, use startLine/endLine on the next call. Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.")]
    private string ReadFile(string path, int? startLine = null, int? endLine = null, bool lineNumbers = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        var ext = Path.GetExtension(fullPath);
        if (!AllowedFileExtensions.IsAllowed(ext))
            return "Error: file type not allowed. Allowed: " + AllowedFileExtensions.List;
        if (!File.Exists(fullPath))
            return "Error: file not found.";
        try
        {
            var lines = File.ReadAllLines(fullPath);
            var totalLines = lines.Length;

            if (startLine.HasValue || endLine.HasValue)
            {
                var from = Math.Max(1, startLine ?? 1) - 1;
                var to = Math.Min(totalLines, endLine ?? totalLines) - 1;
                if (from > to)
                    return $"Error: startLine ({startLine}) is after endLine ({endLine}) or exceeds file length ({totalLines} lines).";
                var selected = lines[from..(to + 1)];
                var body = lineNumbers
                    ? string.Join("\n", selected.Select((l, i) => $"{from + i + 1}: {l}"))
                    : string.Join("\n", selected);
                return $"[Lines {from + 1}–{to + 1} of {totalLines}]\n" + body;
            }

            var fullBody = lineNumbers
                ? string.Join("\n", lines.Select((l, i) => $"{i + 1}: {l}"))
                : string.Join("\n", lines);
            if (totalLines > LargeFileLineThreshold)
                return $"[Total: {totalLines} lines. Tip: use startLine/endLine to read a specific range.]\n" + fullBody;
            return fullBody;
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Write a text file in the workspace. Pass path relative to the workspace root (e.g. notes.txt or src/foo.py) and the content to write. Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Parent directories are created if missing. Overwrites existing files.")]
    private string WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        var ext = Path.GetExtension(fullPath);
        if (!AllowedFileExtensions.IsAllowed(ext))
            return "Error: file type not allowed. Allowed: " + AllowedFileExtensions.List;
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
            return "Written: " + path.Trim();
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Append content to a file in the workspace. path: relative to workspace root (e.g. log.md or results/output.txt). content: text to append; a newline separator is inserted before the new content if the file already has content. Creates the file if it does not exist. Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Use for logs, accumulating results, or building a file incrementally across multiple steps.")]
    private string AppendFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        var ext = Path.GetExtension(fullPath);
        if (!AllowedFileExtensions.IsAllowed(ext))
            return "Error: file type not allowed. Allowed: " + AllowedFileExtensions.List;
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(fullPath))
            {
                var existing = File.ReadAllText(fullPath);
                var separator = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : "";
                File.AppendAllText(fullPath, separator + content, System.Text.Encoding.UTF8);
            }
            else
            {
                File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
            }
            return "Appended to: " + path.Trim();
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("List files and subdirectories in the workspace. Pass an optional path (relative to the workspace root, e.g. src or .) to list a subdirectory; omit or use . for the workspace root. Returns one entry per line: directories end with /, files do not.")]
    private string ListFiles(string? path = null)
    {
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var targetDir = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!targetDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
        if (!Directory.Exists(targetDir))
            return "Error: directory not found.";
        try
        {
            var entries = Directory.GetFileSystemEntries(targetDir)
                .Select(p => (Name: Path.GetFileName(p), IsDir: Directory.Exists(p)))
                .OrderBy(e => !e.IsDir)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.IsDir ? e.Name + "/" : e.Name)
                .ToList();
            return entries.Count == 0 ? "(empty)" : string.Join("\n", entries);
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }
}
