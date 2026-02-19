using SmallEBot.Core;
using System.Text.Json;

namespace SmallEBot.Services.Workspace;

/// <summary>Temp folder and hash index helpers for chunked upload staging.</summary>
public sealed class WorkspaceUploadService
{
    private const string TempRelativeFolder = "temp";
    private const string HashIndexFileName = ".hash-index.json";
    private readonly IVirtualFileSystem _vfs;
    private readonly object _indexLock = new();

    public WorkspaceUploadService(IVirtualFileSystem vfs)
    {
        _vfs = vfs;
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
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
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
            GetTempDirectoryPath(); // ensure temp dir exists
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
