namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Policy for determining restricted paths in the workspace.
/// - sys.skills/, skills/, temp/: ReadFile allowed; write/copy blocked.
/// </summary>
public interface IWorkspaceReadOnlyPolicy
{
    /// <summary>Checks if a relative path is under a restricted directory (no write/copy).</summary>
    bool IsReadOnly(string? relativePath);

    /// <summary>Checks if a relative path is under a restricted directory.</summary>
    bool IsUnder(string? relativePath);

    /// <summary>Checks if ReadFile is blocked for this path. Currently always false (ReadFile allowed everywhere).</summary>
    bool IsBlockedForReadFile(string? relativePath);

    /// <summary>Message shown when a restricted path is accessed for modification.</summary>
    string RestrictedPathMessage { get; }

    /// <summary>Message shown when ReadFile is blocked (unused when IsBlockedForReadFile is always false).</summary>
    string RestrictedReadFileMessage { get; }

    /// <summary>Message shown when searching in a restricted path.</summary>
    string RestrictedSearchMessage { get; }

    /// <summary>Message shown when source path is restricted for copy operations.</summary>
    string RestrictedSourceMessage { get; }

    /// <summary>Message shown when destination path is restricted for copy operations.</summary>
    string RestrictedDestMessage { get; }
}
