namespace SmallEBot.Services.Workspace;

/// <summary>Result of completing an upload. ReplacedOldPath is set when a duplicate (same hash) was deduplicated by renaming the existing file.</summary>
/// <param name="Path">Final workspace-relative path (e.g. temp/foo.txt).</param>
/// <param name="ReplacedOldPath">If not null, an existing attachment with this path was renamed to Path; UI should update the chip.</param>
public sealed record UploadCompleteResult(string Path, string? ReplacedOldPath);

/// <summary>Service for chunked workspace file upload with temp staging and hash deduplication.</summary>
public interface IWorkspaceUploadService
{
    /// <summary>Starts a new chunked upload. Returns upload ID. Throws if file extension is not allowed.</summary>
    /// <param name="fileName">Original file name (used for validation and final path).</param>
    /// <param name="contentLength">Expected total size in bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unique upload ID to use for ReportChunkAsync and CompleteUploadAsync.</returns>
    /// <exception cref="ArgumentException">Thrown when Path.GetExtension(fileName) is not in AllowedFileExtensions.</exception>
    Task<string> StartUploadAsync(string fileName, long contentLength, CancellationToken cancellationToken = default);

    /// <summary>Appends a chunk of data to an in-progress upload.</summary>
    /// <param name="uploadId">Upload ID returned from StartUploadAsync.</param>
    /// <param name="chunk">Chunk bytes to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReportChunkAsync(string uploadId, ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default);

    /// <summary>Completes an upload: closes stream, computes hash, deduplicates, and moves to temp. Returns result or null if upload not found.</summary>
    /// <param name="uploadId">Upload ID from StartUploadAsync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with final path; ReplacedOldPath is set when an existing file was renamed (same hash, different name). Null if upload was not found.</returns>
    Task<UploadCompleteResult?> CompleteUploadAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an in-progress upload and removes staging file.</summary>
    /// <param name="uploadId">Upload ID to cancel.</param>
    void CancelUpload(string uploadId);
}
