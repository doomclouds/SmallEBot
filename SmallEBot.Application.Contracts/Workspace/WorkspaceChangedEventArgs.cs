// SmallEBot.Application.Contracts/Workspace/WorkspaceChangedEventArgs.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Event arguments for workspace change events.
/// </summary>
public record WorkspaceChangedEventArgs(string[] ChangedPaths);
