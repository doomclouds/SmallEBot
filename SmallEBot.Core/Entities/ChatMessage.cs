using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ChatMessage : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? ReplacedByMessageId { get; set; }
    public bool IsEdited { get; set; }

    /// <summary>JSON array of attached file paths (e.g. ["path/to/file.md"]). User messages only.</summary>
    public string? AttachedPathsJson { get; set; }
    /// <summary>JSON array of requested skill ids (e.g. ["dotnet-skills"]). User messages only.</summary>
    public string? RequestedSkillIdsJson { get; set; }

    [NotMapped]
    public IReadOnlyList<string> AttachedPaths =>
        string.IsNullOrEmpty(AttachedPathsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(AttachedPathsJson) ?? [];

    [NotMapped]
    public IReadOnlyList<string> RequestedSkillIds =>
        string.IsNullOrEmpty(RequestedSkillIdsJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(RequestedSkillIdsJson) ?? [];

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
