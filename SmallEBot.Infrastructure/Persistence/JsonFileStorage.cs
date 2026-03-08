using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SmallEBot.Application.Contracts.Persistence;

namespace SmallEBot.Infrastructure.Persistence;

/// <summary>
/// Thread-safe JSON file storage implementation using SemaphoreSlim for async-safe locking.
/// Files are stored at: {basePath}/{key}.json
/// </summary>
/// <typeparam name="T">The entity type to store.</typeparam>
public sealed class JsonFileStorage<T> : IJsonFileStorage<T> where T : class
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of JsonFileStorage.
    /// </summary>
    /// <param name="basePath">The base directory path for storing JSON files.</param>
    public JsonFileStorage(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        // Ensure directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    /// <inheritdoc />
    public async Task<T?> LoadAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSafeFilePath(key);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string key, T entity, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);

        var filePath = GetSafeFilePath(key);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(entity, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSafeFilePath(key);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            File.Delete(filePath);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> LoadAllAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var files = Directory.GetFiles(_basePath, "*.json");
            var results = new List<T>(files.Length);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var entity = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                if (entity is not null)
                {
                    results.Add(entity);
                }
            }

            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSafeFilePath(key);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return File.Exists(filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the full file path for a given key with path traversal protection.
    /// </summary>
    /// <param name="key">The entity key.</param>
    /// <returns>The full file path.</returns>
    /// <exception cref="ArgumentException">Thrown if key is invalid or contains path traversal attempts.</exception>
    private string GetSafeFilePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        // Sanitize key: remove invalid filename characters
        var sanitizedKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));

        if (string.IsNullOrWhiteSpace(sanitizedKey))
        {
            throw new ArgumentException("Key results in empty filename after sanitization.", nameof(key));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, $"{sanitizedKey}.json"));

        // Ensure the final path is still within the base directory (prevent path traversal)
        var basePathRoot = Path.GetFullPath(_basePath);
        if (!fullPath.StartsWith(basePathRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Key contains invalid path traversal characters.", nameof(key));
        }

        return fullPath;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(JsonFileStorage<T>));
        }
    }

    /// <summary>
    /// Releases all resources used by the JsonFileStorage.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Dispose();
        _disposed = true;
    }
}
