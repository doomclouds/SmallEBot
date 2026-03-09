namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Default implementation of workspace restricted-path policy.
/// - sys.skills, skills, temp: ReadFile allowed; write/copy blocked.
/// </summary>
public sealed class WorkspaceReadOnlyPolicy : IWorkspaceReadOnlyPolicy
{
    /// <inheritdoc />
    public string RestrictedPathMessage => "Error: Path is restricted (sys.skills, skills, temp). Write/copy not allowed.";

    /// <inheritdoc />
    public string RestrictedReadFileMessage => "Error: ReadFile is blocked for this path.";

    /// <inheritdoc />
    public string RestrictedSearchMessage => "Error: Cannot search under sys.skills/, skills/, or temp/.";

    /// <inheritdoc />
    public string RestrictedSourceMessage => "Error: Source path is restricted (sys.skills, skills, temp).";

    /// <inheritdoc />
    public string RestrictedDestMessage => "Error: Destination path is restricted (sys.skills, skills, temp).";

    private static readonly string[] RestrictedPaths = ["sys.skills", "skills", "temp"];

    /// <inheritdoc />
    public bool IsReadOnly(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        return RestrictedPaths.Any(rp =>
            normalizedPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) &&
            (normalizedPath.Length == rp.Length || normalizedPath[rp.Length] == '/'));
    }

    /// <inheritdoc />
    public bool IsUnder(string? relativePath) => IsReadOnly(relativePath);

    /// <inheritdoc />
    public bool IsBlockedForReadFile(string? relativePath) => false;
}
