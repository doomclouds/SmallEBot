namespace SmallEBot.Services.Workspace;

/// <summary>Paths under the workspace (VFS) that are read-only: view and list only, no delete or write.</summary>
public static class WorkspaceReadOnly
{
    /// <summary>System skills directory name under VFS root.</summary>
    public const string SysSkillsDir = "sys.skills";

    /// <summary>User skills directory name under VFS root.</summary>
    public const string UserSkillsDir = "skills";

    /// <summary>True if the given workspace-relative path is under sys.skills or skills (read-only area).</summary>
    public static bool IsUnder(string? relativePath)
    {
        var n = relativePath?.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(n)) return false;
        return n.StartsWith(SysSkillsDir + "/", StringComparison.OrdinalIgnoreCase)
               || n.Equals(SysSkillsDir, StringComparison.OrdinalIgnoreCase)
               || n.StartsWith(UserSkillsDir + "/", StringComparison.OrdinalIgnoreCase)
               || n.Equals(UserSkillsDir, StringComparison.OrdinalIgnoreCase);
    }
}
