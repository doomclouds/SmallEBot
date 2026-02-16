namespace SmallEBot.Core.Models;

/// <summary>Marks an entity that has a creation time for timeline ordering.</summary>
public interface ICreateTime
{
    DateTime CreatedAt { get; }
}
