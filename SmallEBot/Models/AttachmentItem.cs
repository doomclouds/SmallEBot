namespace SmallEBot.Models;

/// <summary>Base type for chat attachment items shown as chips (resolved path or pending upload).</summary>
public abstract record AttachmentItem;

/// <summary>An attachment that is already available at a workspace path.</summary>
public sealed record ResolvedPathAttachment(string Path) : AttachmentItem;

/// <summary>An attachment whose file is still being uploaded.</summary>
public sealed record PendingUploadAttachment(string UploadId, string DisplayName, int Progress = 0) : AttachmentItem
{
    public int Progress { get; set; } = Progress;
}
