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

    [Description("Read a text file from the workspace. Pass path relative to the workspace root (e.g. notes.txt or src/script.py). Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.")]
    private string ReadFile(string path)
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
            return File.ReadAllText(fullPath);
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
            return "Written " + fullPath;
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
