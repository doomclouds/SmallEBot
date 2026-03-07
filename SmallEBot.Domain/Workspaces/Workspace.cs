// SmallEBot.Domain/Workspaces/Workspace.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Aggregate root for workspace operations.
/// Manages the virtual file system for the application.
/// </summary>
public class Workspace(string rootPath) : IAggregateRoot
{
    public string RootPath { get; } = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
}
