namespace SmallEBot.Models;

/// <summary>Result from EditMessageDialog: content plus optional attachments and skill references.</summary>
public sealed record EditMessageResult(string Content, List<string> AttachedPaths, List<string> RequestedSkillIds);
