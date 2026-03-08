namespace SmallEBot.Models;

/// <summary>Arguments passed when user submits an edit: used for optimistic UI update.</summary>
public sealed record EditSubmittedArgs(
    Guid TurnId,
    string NewContent,
    IReadOnlyList<string> AttachedPaths,
    IReadOnlyList<string> RequestedSkillIds,
    DateTime OriginalCreatedAt);
