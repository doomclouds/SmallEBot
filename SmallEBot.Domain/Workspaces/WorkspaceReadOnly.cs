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
    public static readonly string[] ReadOnlyPaths = ["sys.skills", "skills", "temp"];

    /// <summary>
    /// Message shown when a read-only path is accessed for modification.
    /// </summary>
    public const string RestrictedPathMessage = "Error: Path is read-only (sys.skills, skills, temp).";

    /// <summary>
    /// Message shown when searching in a read-only path.
    /// </summary>
    public const string RestrictedSearchMessage = "Error: Cannot search under sys.skills/, skills/, or temp/.";

    /// <summary>
    /// Message shown when source path is read-only for copy operations.
    /// </summary>
    public const string RestrictedSourceMessage = "Error: Source path is read-only (sys.skills, skills, temp).";

    /// <summary>
    /// Message shown when destination path is read-only for copy operations.
    /// </summary>
    public const string RestrictedDestMessage = "Error: Destination path is read-only (sys.skills, skills, temp).";

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

    /// <summary>
    /// Checks if a relative path is under a read-only directory.
    /// </summary>
    /// <param name="relativePath">The relative path to check.</param>
    /// <returns>True if the path is under a read-only directory.</returns>
    public static bool IsUnder(string? relativePath)
    {
        return IsReadOnly(relativePath);
    }
}
