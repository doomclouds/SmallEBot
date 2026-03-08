namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Policy for determining read-only paths in the workspace.
/// Use for testability and extensibility; default implementation in <see cref="WorkspaceReadOnlyPolicy"/>.
/// </summary>
public interface IWorkspaceReadOnlyPolicy
{
    /// <summary>Checks if a relative path is read-only.</summary>
    bool IsReadOnly(string? relativePath);

    /// <summary>Checks if a relative path is under a read-only directory.</summary>
    bool IsUnder(string? relativePath);

    /// <summary>Message shown when a read-only path is accessed for modification.</summary>
    string RestrictedPathMessage { get; }

    /// <summary>Message shown when searching in a read-only path.</summary>
    string RestrictedSearchMessage { get; }

    /// <summary>Message shown when source path is read-only for copy operations.</summary>
    string RestrictedSourceMessage { get; }

    /// <summary>Message shown when destination path is read-only for copy operations.</summary>
    string RestrictedDestMessage { get; }
}
