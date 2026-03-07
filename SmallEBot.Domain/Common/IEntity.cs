// SmallEBot.Domain/Common/IEntity.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Base interface for entities with identity.
/// </summary>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public interface IEntity<out TId> where TId : notnull
{
    TId Id { get; }
}
