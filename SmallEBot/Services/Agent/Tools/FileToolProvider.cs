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
        BuiltInToolNames.WriteFile        => TimeSpan.FromMinutes(10),
        BuiltInToolNames.ReadFile         => TimeSpan.FromSeconds(30),
        BuiltInToolNames.ListFiles        => TimeSpan.FromSeconds(30),
        BuiltInToolNames.GetWorkspaceRoot => TimeSpan.FromSeconds(5),
        BuiltInToolNames.CopyFile         => TimeSpan.FromSeconds(30),
        BuiltInToolNames.CopyDirectory    => TimeSpan.FromMinutes(5),
        _ => null
    };

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GetWorkspaceRoot);
        yield return AIFunctionFactory.Create(ReadFile);
        yield return AIFunctionFactory.Create(WriteFile);
        yield return AIFunctionFactory.Create(AppendFile);
        yield return AIFunctionFactory.Create(ListFiles);
        yield return AIFunctionFactory.Create(CopyFile);
        yield return AIFunctionFactory.Create(CopyDirectory);
    }

    [Description("Get the absolute path of the workspace (virtual file system root). No parameters. Use when a tool or MCP requires an absolute path (e.g. MCP get_document savePath, or script arguments). Call once at the start of a multi-step flow and reuse the returned path.")]
    private string GetWorkspaceRoot()
    {
        try
        {
            return Path.GetFullPath(vfs.GetRootPath());
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Read a text file from the workspace. path: relative to workspace root (e.g. notes.txt or src/script.py). startLine/endLine: optional 1-based line numbers to read only part of a large file (e.g. startLine=10, endLine=50). lineNumbers: if true, prefix each output line with its 1-based number — useful when cross-referencing GrepContent results. When the response header says [Total: N lines] and N is large, use startLine/endLine on the next call. Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Paths under sys.skills/, skills/, or temp/ are not allowed.")]
    private string ReadFile(string path, int? startLine = null, int? endLine = null, bool lineNumbers = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var norm = path.Trim().Replace('\\', '/').TrimStart('/');
        if (WorkspaceReadOnly.IsUnder(norm))
            return WorkspaceReadOnly.RestrictedPathMessage;
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

    [Description("Write a text file in the workspace. Pass path relative to the workspace root (e.g. notes.txt or src/foo.py) and the content to write. Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Parent directories are created if missing. Overwrites existing files. Paths under sys.skills/, skills/, or temp/ are not allowed.")]
    private string WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var norm = path.Trim().Replace('\\', '/').TrimStart('/');
        if (WorkspaceReadOnly.IsUnder(norm))
            return WorkspaceReadOnly.RestrictedPathMessage;
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

    [Description("Append content to a file in the workspace. path: relative to workspace root (e.g. log.md or results/output.txt). content: text to append; a newline separator is inserted before the new content if the file already has content. Creates the file if it does not exist. Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Paths under sys.skills/, skills/, or temp/ are not allowed.")]
    private string AppendFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var norm = path.Trim().Replace('\\', '/').TrimStart('/');
        if (WorkspaceReadOnly.IsUnder(norm))
            return WorkspaceReadOnly.RestrictedPathMessage;
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

    [Description("Copy a single file within the workspace. sourcePath: file to copy (relative to workspace root, e.g. docs/readme.md). destPath: destination file path (relative to workspace root); parent directories are created if missing. Overwrites if destination already exists. Both paths must be under the workspace. Source and destination cannot be under sys.skills, skills, or temp. Allowed extensions only. Fails if source is not a file or has a disallowed extension.")]
    private string CopyFile(string sourcePath, string destPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return "Error: sourcePath is required.";
        if (string.IsNullOrWhiteSpace(destPath)) return "Error: destPath is required.";
        var sourceNorm = sourcePath.Trim().Replace('\\', '/').TrimStart('/');
        var destNorm = destPath.Trim().Replace('\\', '/').TrimStart('/');
        if (WorkspaceReadOnly.IsUnder(sourceNorm))
            return WorkspaceReadOnly.RestrictedSourceMessage;
        if (WorkspaceReadOnly.IsUnder(destNorm))
            return WorkspaceReadOnly.RestrictedDestMessage;
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var sourceFull = Path.GetFullPath(Path.Combine(baseDir, sourcePath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        var destFull = Path.GetFullPath(Path.Combine(baseDir, destPath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!sourceFull.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: sourcePath must be under the workspace.";
        if (!destFull.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: destPath must be under the workspace.";
        if (!File.Exists(sourceFull))
            return "Error: source file not found.";
        if (Directory.Exists(sourceFull))
            return "Error: source is a directory; use CopyDirectory for directories.";
        var sourceExt = Path.GetExtension(sourceFull);
        if (!AllowedFileExtensions.IsAllowed(sourceExt))
            return "Error: source file type not allowed. Allowed: " + AllowedFileExtensions.List;
        var destExt = Path.GetExtension(destFull);
        if (!AllowedFileExtensions.IsAllowed(destExt))
            return "Error: destination file type not allowed. Allowed: " + AllowedFileExtensions.List;
        try
        {
            var destDir = Path.GetDirectoryName(destFull);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);
            File.Copy(sourceFull, destFull, overwrite: true);
            return "Copied: " + sourcePath.Trim() + " → " + destPath.Trim();
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Copy a directory from one path to another within the workspace. sourcePath: directory to copy (relative to workspace root, e.g. docs or backup/2024). destPath: destination directory (relative to workspace root); created if missing. Copies all files and subdirectories recursively. Both paths must be under the workspace. Source and destination cannot be under sys.skills, skills, or temp. Fails if source does not exist, is not a directory, or if dest is inside source (to avoid infinite copy).")]
    private string CopyDirectory(string sourcePath, string destPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return "Error: sourcePath is required.";
        if (string.IsNullOrWhiteSpace(destPath)) return "Error: destPath is required.";
        var sourceNorm = sourcePath.Trim().Replace('\\', '/').TrimStart('/');
        var destNorm = destPath.Trim().Replace('\\', '/').TrimStart('/');
        if (WorkspaceReadOnly.IsUnder(sourceNorm))
            return WorkspaceReadOnly.RestrictedSourceMessage;
        if (WorkspaceReadOnly.IsUnder(destNorm))
            return WorkspaceReadOnly.RestrictedDestMessage;
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var sourceFull = Path.GetFullPath(Path.Combine(baseDir, sourcePath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        var destFull = Path.GetFullPath(Path.Combine(baseDir, destPath.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!sourceFull.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: sourcePath must be under the workspace.";
        if (!destFull.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: destPath must be under the workspace.";
        if (!Directory.Exists(sourceFull))
            return "Error: source directory not found.";
        if (destFull.StartsWith(sourceFull.TrimEnd(Path.DirectorySeparatorChar, '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || destFull.Equals(sourceFull, StringComparison.OrdinalIgnoreCase))
            return "Error: destination cannot be inside or equal to source.";
        try
        {
            Directory.CreateDirectory(destFull);
            var count = CopyDirectoryRecursive(sourceFull, destFull);
            return $"Copied {count} file(s) from {sourcePath.Trim()} to {destPath.Trim()}.";
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    private static int CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        var count = 0;
        foreach (var entry in Directory.GetFileSystemEntries(sourceDir))
        {
            var name = Path.GetFileName(entry);
            var destPath = Path.Combine(destDir, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(destPath);
                count += CopyDirectoryRecursive(entry, destPath);
            }
            else
            {
                File.Copy(entry, destPath, overwrite: true);
                count++;
            }
        }
        return count;
    }

    [Description("List files and subdirectories in the workspace. Pass an optional path (relative to the workspace root, e.g. src or .) to list a subdirectory; omit or use . for the workspace root. Returns one entry per line: directories end with /, files do not. Paths under sys.skills/, skills/, or temp/ are not allowed.")]
    private string ListFiles(string? path = null)
    {
        var pathNorm = (path?.Trim() ?? ".").Replace('\\', '/').TrimStart('/');
        if (!string.IsNullOrEmpty(pathNorm) && pathNorm != "." && WorkspaceReadOnly.IsUnder(pathNorm))
            return WorkspaceReadOnly.RestrictedPathMessage;
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
