namespace SmallEBot.Services.Workspace;

/// <summary>Paths under the workspace (VFS) that must not be accessed by agent file tools: skills (use skill tools only) and temp (upload-only).</summary>
public static class WorkspaceReadOnly
{
    /// <summary>System skills directory name under VFS root.</summary>
    public const string SysSkillsDir = "sys.skills";

    /// <summary>User skills directory name under VFS root.</summary>
    public const string UserSkillsDir = "skills";

    /// <summary>Temp directory under VFS root; reserved for file uploads. Agent must not use file tools on it.</summary>
    public const string TempDir = "temp";

    // ── Error messages (single place for prompt/UX changes) ───────────────────

    /// <summary>Error message when a single path (read/write/list) is under a restricted area.</summary>
    public static string RestrictedPathMessage => "Error: path is under a restricted area (sys.skills, skills, or temp). Use skill tools for skills; do not access temp (upload-only).";

    /// <summary>Error message when source path is under a restricted area (e.g. CopyFile, CopyDirectory).</summary>
    public static string RestrictedSourceMessage => "Error: source cannot be under a restricted area (sys.skills, skills, or temp).";

    /// <summary>Error message when destination path is under a restricted area (e.g. CopyFile, CopyDirectory).</summary>
    public static string RestrictedDestMessage => "Error: destination cannot be under a restricted area (sys.skills, skills, or temp).";

    /// <summary>Error message when search path is under a restricted area (GrepFiles, GrepContent).</summary>
    public static string RestrictedSearchMessage => "Error: path is under a restricted area (sys.skills, skills, or temp). Do not search temp (upload-only) or skills with this tool.";

    /// <summary>True if the given workspace-relative path is under sys.skills, skills, or temp (no ReadFile/WriteFile/ListFiles/etc.).</summary>
    public static bool IsUnder(string? relativePath)
    {
        var n = relativePath?.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(n)) return false;
        return IsUnderSegment(n, SysSkillsDir)
               || IsUnderSegment(n, UserSkillsDir)
               || IsUnderSegment(n, TempDir);
    }

    private static bool IsUnderSegment(string normalized, string segment)
    {
        return normalized.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(segment, StringComparison.OrdinalIgnoreCase);
    }
}
