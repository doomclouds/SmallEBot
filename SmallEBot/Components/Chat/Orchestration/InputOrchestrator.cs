using SmallEBot.Application.Contracts.Agents.Skills;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Orchestration;

/// <summary>
/// Shared logic for attachment and skill management in chat input.
/// Used by ChatInput and EditMessageDialog. Not registered in DI; components instantiate with injected services.
/// </summary>
public class InputOrchestrator
{
    private readonly IWorkspaceUploadService? _uploadService;

    public InputOrchestrator(IWorkspaceService workspaceService, ISkillsConfigService skillsConfigService, IWorkspaceUploadService? uploadService = null)
    {
        _uploadService = uploadService;
    }

    public string InputText { get; set; } = "";
    public List<AttachmentItem> Attachments { get; } = [];
    public List<string> RequestedSkillIds { get; } = [];

    public event Action? OnStateChanged;

    public Task HandleInputChangedAsync(string value)
    {
        InputText = value;
        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Attachments.OfType<ResolvedPathAttachment>().All(x => x.Path != path))
                Attachments.Add(new ResolvedPathAttachment(path));
        }
        OnStateChanged?.Invoke();
    }

    public void AddSkills(IEnumerable<string> skillIds)
    {
        foreach (var id in skillIds)
        {
            if (!RequestedSkillIds.Any(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase)))
                RequestedSkillIds.Add(id);
        }
        OnStateChanged?.Invoke();
    }

    public void RemoveAttachment(AttachmentItem item)
    {
        if (item is PendingUploadAttachment pending)
            _uploadService?.CancelUpload(pending.UploadId);
        Attachments.Remove(item);
        OnStateChanged?.Invoke();
    }

    public void RemoveSkill(string skillId)
    {
        RequestedSkillIds.Remove(skillId);
        OnStateChanged?.Invoke();
    }

    /// <summary>Add a pending upload attachment (for drop zone).</summary>
    public void AddPendingUpload(string uploadId, string fileName)
    {
        Attachments.Add(new PendingUploadAttachment(uploadId, fileName));
        OnStateChanged?.Invoke();
    }

    /// <summary>Update progress of a pending upload.</summary>
    public void ReportUploadProgress(string uploadId, int progress)
    {
        var pending = Attachments.OfType<PendingUploadAttachment>().FirstOrDefault(p => p.UploadId == uploadId);
        if (pending != null)
        {
            pending.Progress = progress;
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>Complete upload: remove pending, add resolved path, optionally replace old path.</summary>
    public void OnUploadComplete(string uploadId, string path, string? replacedOldPath)
    {
        var idx = Attachments.FindIndex(x => x is PendingUploadAttachment p && p.UploadId == uploadId);
        if (idx >= 0)
            Attachments.RemoveAt(idx);
        if (string.IsNullOrEmpty(path)) return;
        if (!string.IsNullOrEmpty(replacedOldPath))
            Attachments.RemoveAll(x => x is ResolvedPathAttachment r && r.Path == replacedOldPath);
        if (Attachments.OfType<ResolvedPathAttachment>().All(r => r.Path != path))
            Attachments.Add(new ResolvedPathAttachment(path));
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Collect current input and attachments, then reset for next send.
    /// </summary>
    public (string Text, List<string> AttachedPaths, List<string> RequestedSkillIds) Collect()
    {
        var text = InputText;
        var attachedPaths = Attachments.OfType<ResolvedPathAttachment>().Select(x => x.Path).ToList();
        var requestedSkillIds = RequestedSkillIds.ToList();

        InputText = "";
        Attachments.RemoveAll(_ => _ is ResolvedPathAttachment);
        RequestedSkillIds.Clear();
        OnStateChanged?.Invoke();

        return (text, attachedPaths, requestedSkillIds);
    }

    public void Reset()
    {
        InputText = "";
        Attachments.Clear();
        RequestedSkillIds.Clear();
        OnStateChanged?.Invoke();
    }

    public void InitializeFrom(string text, IEnumerable<AttachmentItem> attachments, IEnumerable<string> skillIds)
    {
        InputText = text;
        Attachments.Clear();
        Attachments.AddRange(attachments);
        RequestedSkillIds.Clear();
        RequestedSkillIds.AddRange(skillIds);
        OnStateChanged?.Invoke();
    }
}
