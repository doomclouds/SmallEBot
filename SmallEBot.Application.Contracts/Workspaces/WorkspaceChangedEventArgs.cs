// SmallEBot.Application.Contracts/Workspaces/WorkspaceChangedEventArgs.cs
namespace SmallEBot.Application.Contracts.Workspaces;

/// <summary>
/// Event arguments for workspace change events.
/// </summary>
public record WorkspaceChangedEventArgs(string[] ChangedPaths);
