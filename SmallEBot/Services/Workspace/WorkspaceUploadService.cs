using System.Linq;
using SmallEBot.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmallEBot.Services.Workspace;

/// <summary>Temp folder and hash index helpers for chunked upload staging. Implements IWorkspaceUploadService.</summary>
public sealed class WorkspaceUploadService : IWorkspaceUploadService
{
    private const string TempRelativeFolder = "temp";
    private const string HashIndexFileName = ".hash-index.json";
    private readonly IVirtualFileSystem _vfs;
    private readonly object _indexLock = new();
    private readonly Dictionary<string, (string StagingPath, string FileName, long ContentLength, FileStream? Stream)> _uploads = [];
    private readonly object _uploadsLock = new();
    private bool _cleanedOrphans;

    public WorkspaceUploadService(IVirtualFileSystem vfs)
    {
        _vfs = vfs;
    }

    /// <inheritdoc />
    public Task<string> StartUploadAsync(string fileName, long contentLength, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName);
        if (!AllowedFileExtensions.IsAllowed(ext))
            throw new ArgumentException($"File extension '{ext}' is not allowed. Allowed: {AllowedFileExtensions.List}.", nameof(fileName));

        if (!_cleanedOrphans)
        {
            CleanupOrphanStagingFiles();
            _cleanedOrphans = true;
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var stagingPath = GetStagingPath(uploadId);
        var stream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);

        lock (_uploadsLock)
        {
            _uploads[uploadId] = (stagingPath, fileName, contentLength, stream);
        }

        return Task.FromResult(uploadId);
    }

    /// <inheritdoc />
    public Task ReportChunkAsync(string uploadId, ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default)
    {
        FileStream? stream;
        lock (_uploadsLock)
        {
            if (!_uploads.TryGetValue(uploadId, out var record))
                return Task.CompletedTask;
            stream = record.Stream;
        }

        stream?.Write(chunk.Span);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> CompleteUploadAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        (string StagingPath, string FileName, long ContentLength, FileStream? Stream) record;
        lock (_uploadsLock)
        {
            if (!_uploads.Remove(uploadId, out record))
                return Task.FromResult<string?>(null);
        }

        record.Stream?.Close();
        record.Stream?.Dispose();

        var sanitizedFileName = Path.GetFileName(record.FileName);
        var targetRelativePath = "temp/" + sanitizedFileName;
        var rootPath = _vfs.GetRootPath();
        var targetFullPath = ResolveFullPath(rootPath, targetRelativePath);

        string hash;
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(record.StagingPath))
        {
            hash = Convert.ToHexString(sha.ComputeHash(fs));
        }

        // Index format: path -> hash (design). When renaming, remove old path key and add new path key.
        Dictionary<string, string> index;
        lock (_indexLock)
        {
            index = LoadHashIndex();
            var existingPath = index.FirstOrDefault(kv => string.Equals(kv.Value, hash, StringComparison.OrdinalIgnoreCase)).Key;
            if (existingPath != null)
            {
                var existingFullPath = ResolveFullPath(rootPath, existingPath);
                if (File.Exists(existingFullPath) && existingPath != targetRelativePath)
                {
                    File.Delete(record.StagingPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
                    File.Move(existingFullPath, targetFullPath, overwrite: true);
                    index.Remove(existingPath);
                    index[targetRelativePath] = hash;
                }
                else if (!File.Exists(existingFullPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
                    File.Move(record.StagingPath, targetFullPath, overwrite: true);
                    index.Remove(existingPath);
                    index[targetRelativePath] = hash;
                }
                else
                {
                    File.Delete(record.StagingPath);
                    if (existingPath != targetRelativePath)
                    {
                        index.Remove(existingPath);
                        index[targetRelativePath] = hash;
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
                File.Move(record.StagingPath, targetFullPath, overwrite: true);
                index[targetRelativePath] = hash;
            }
            SaveHashIndex(index);
        }

        return Task.FromResult<string?>(targetRelativePath);
    }

    /// <inheritdoc />
    public void CancelUpload(string uploadId)
    {
        (string StagingPath, string FileName, long ContentLength, FileStream? Stream) record;
        lock (_uploadsLock)
        {
            if (!_uploads.Remove(uploadId, out record))
                return;
        }

        record.Stream?.Dispose();
        if (File.Exists(record.StagingPath))
            File.Delete(record.StagingPath);
    }

    private void CleanupOrphanStagingFiles()
    {
        var tempDir = GetTempDirectoryPath();
        try
        {
            foreach (var f in Directory.EnumerateFiles(tempDir, ".upload-*"))
            {
                try
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(f);
                    if (age.TotalMinutes > 30)
                        File.Delete(f);
                }
                catch
                {
                    // ignore per-file errors
                }
            }
        }
        catch
        {
            // ignore if temp dir doesn't exist yet
        }
    }

    private static string ResolveFullPath(string rootPath, string workspaceRelativePath)
    {
        var parts = workspaceRelativePath.Replace('\\', '/').Split('/');
        return Path.GetFullPath(Path.Combine([rootPath, ..parts]));
    }

    /// <summary>Returns the absolute path to the temp folder under workspace root. Ensures the directory exists.</summary>
    public string GetTempDirectoryPath()
    {
        var path = Path.Combine(_vfs.GetRootPath(), TempRelativeFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Returns the absolute path to the hash index file.</summary>
    public string GetHashIndexPath()
    {
        return Path.Combine(GetTempDirectoryPath(), HashIndexFileName);
    }

    /// <summary>Loads the hash index from disk. Returns empty dictionary if file does not exist or deserialization fails.</summary>
    public Dictionary<string, string> LoadHashIndex()
    {
        lock (_indexLock)
        {
            var indexPath = GetHashIndexPath();
            if (!File.Exists(indexPath))
                return new Dictionary<string, string>();

            try
            {
                var json = File.ReadAllText(indexPath);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
                // Normalize to path->hash: migrate old hash->path format (key is hex, value is path)
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in raw)
                {
                    if (kv.Key.Contains('/'))
                        result[kv.Key] = kv.Value;
                    else
                        result[kv.Value] = kv.Key;
                }
                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>Persists the hash index to disk.</summary>
    /// <param name="index">The index to save; must not be null.</param>
    public void SaveHashIndex(Dictionary<string, string> index)
    {
        ArgumentNullException.ThrowIfNull(index);
        lock (_indexLock)
        {
            GetTempDirectoryPath();
            var json = JsonSerializer.Serialize(index);
            File.WriteAllText(GetHashIndexPath(), json);
        }
    }

    /// <summary>Returns the absolute path to the staging file for the given upload ID.</summary>
    /// <param name="uploadId">Unique identifier for the upload; must not be null or empty.</param>
    public string GetStagingPath(string uploadId)
    {
        if (string.IsNullOrEmpty(uploadId))
            throw new ArgumentException("Upload ID must not be null or empty.", nameof(uploadId));
        return Path.Combine(GetTempDirectoryPath(), ".upload-" + uploadId);
    }
}
