namespace SmallEBot.Models;

/// <summary>Base type for chat attachment items shown as chips (resolved path or pending upload).</summary>
public abstract record AttachmentItem;

/// <summary>An attachment that is already available at a workspace path.</summary>
public sealed record ResolvedPathAttachment(string Path) : AttachmentItem;

/// <summary>An attachment whose file is still being uploaded.</summary>
public sealed record PendingUploadAttachment : AttachmentItem
{
    public string UploadId { get; }
    public string DisplayName { get; }
    public int Progress { get; set; }

    public PendingUploadAttachment(string uploadId, string displayName, int progress = 0)
    {
        UploadId = uploadId;
        DisplayName = displayName;
        Progress = progress;
    }
}
