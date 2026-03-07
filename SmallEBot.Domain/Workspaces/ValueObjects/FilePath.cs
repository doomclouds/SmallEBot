// SmallEBot.Domain/Workspaces/ValueObjects/FilePath.cs
namespace SmallEBot.Domain.Workspaces.ValueObjects;

/// <summary>
/// Represents a file path within the workspace.
/// </summary>
/// <param name="RelativePath">Path relative to workspace root.</param>
public record FilePath(string RelativePath)
{
    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public string Extension => Path.GetExtension(RelativePath);

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>
    /// Gets the directory path.
    /// </summary>
    public string? DirectoryPath => Path.GetDirectoryName(RelativePath);
}
