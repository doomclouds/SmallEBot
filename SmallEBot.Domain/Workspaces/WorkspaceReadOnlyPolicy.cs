namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Default implementation of workspace restricted-path policy.
/// - sys.skills, skills: use load_skill/read_skill_resource; ReadFile blocked.
/// - temp: uploads; ReadFile allowed, write/copy blocked.
/// </summary>
public sealed class WorkspaceReadOnlyPolicy : IWorkspaceReadOnlyPolicy
{
    /// <inheritdoc />
    public string RestrictedPathMessage => "Error: Path is restricted (sys.skills, skills, temp). Write/copy not allowed.";

    /// <inheritdoc />
    public string RestrictedReadFileMessage => "Error: Use load_skill(skillId) and read_skill_resource(skillId, path) for sys.skills/ and skills/ content. ReadFile cannot access these paths.";

    /// <inheritdoc />
    public string RestrictedSearchMessage => "Error: Cannot search under sys.skills/, skills/, or temp/.";

    /// <inheritdoc />
    public string RestrictedSourceMessage => "Error: Source path is restricted (sys.skills, skills, temp).";

    /// <inheritdoc />
    public string RestrictedDestMessage => "Error: Destination path is restricted (sys.skills, skills, temp).";

    private static readonly string[] RestrictedPaths = ["sys.skills", "skills", "temp"];
    private static readonly string[] ReadFileBlockedPaths = ["sys.skills", "skills"];

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
    public bool IsBlockedForReadFile(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        return ReadFileBlockedPaths.Any(rp =>
            normalizedPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) &&
            (normalizedPath.Length == rp.Length || normalizedPath[rp.Length] == '/'));
    }
}
