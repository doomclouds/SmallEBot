// SmallEBot.Core/Models/TurnMetadata.cs
namespace SmallEBot.Core.Models;

/// <summary>
/// Minimal turn metadata stored alongside AgentSession.
/// Only contains data not available in AgentSession (attachments, skills).
/// </summary>
public class TurnMetadata
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> AttachedPaths { get; set; } = [];
    public List<string> RequestedSkillIds { get; set; } = [];
}
