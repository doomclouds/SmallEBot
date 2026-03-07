// SmallEBot.Domain/Workspaces/WorkspaceReadOnly.cs
namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Defines read-only paths in the workspace.
/// </summary>
public static class WorkspaceReadOnly
{
    /// <summary>
    /// Paths that are read-only in the workspace (skills, system files).
    /// </summary>
    public static readonly string[] ReadOnlyPaths = ["sys.skills", "skills"];

    /// <summary>
    /// Checks if a relative path is read-only.
    /// </summary>
    /// <param name="relativePath">The relative path to check.</param>
    /// <returns>True if the path is read-only.</returns>
    public static bool IsReadOnly(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        return ReadOnlyPaths.Any(rp =>
            normalizedPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Equals(rp, StringComparison.OrdinalIgnoreCase));
    }
}
