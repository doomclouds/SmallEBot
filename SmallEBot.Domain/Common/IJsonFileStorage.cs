namespace SmallEBot.Domain.Common;

/// <summary>
/// Generic interface for JSON file-based storage operations.
/// </summary>
/// <typeparam name="T">The entity type to store.</typeparam>
public interface IJsonFileStorage<T> : IDisposable where T : class
{
    /// <summary>
    /// Loads an entity by its key.
    /// </summary>
    /// <param name="key">The unique key identifying the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    Task<T?> LoadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Saves an entity with the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the entity.</param>
    /// <param name="entity">The entity to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(string key, T entity, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity by its key.
    /// </summary>
    /// <param name="key">The unique key identifying the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the entity was deleted, false if it didn't exist.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Loads all entities from storage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of all entities.</returns>
    Task<IReadOnlyList<T>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if an entity exists with the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the entity exists, otherwise false.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
