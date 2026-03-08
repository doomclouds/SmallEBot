namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Default implementation of workspace read-only policy.
/// Paths sys.skills, skills, temp are read-only.
/// </summary>
public sealed class WorkspaceReadOnlyPolicy : IWorkspaceReadOnlyPolicy
{
    /// <inheritdoc />
    public string RestrictedPathMessage => "Error: Path is read-only (sys.skills, skills, temp).";

    /// <inheritdoc />
    public string RestrictedSearchMessage => "Error: Cannot search under sys.skills/, skills/, or temp/.";

    /// <inheritdoc />
    public string RestrictedSourceMessage => "Error: Source path is read-only (sys.skills, skills, temp).";

    /// <inheritdoc />
    public string RestrictedDestMessage => "Error: Destination path is read-only (sys.skills, skills, temp).";

    private static readonly string[] ReadOnlyPaths = ["sys.skills", "skills", "temp"];

    /// <inheritdoc />
    public bool IsReadOnly(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        return ReadOnlyPaths.Any(rp =>
            normalizedPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Equals(rp, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public bool IsUnder(string? relativePath) => IsReadOnly(relativePath);
}
