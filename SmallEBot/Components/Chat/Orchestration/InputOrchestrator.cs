using SmallEBot.Application.Contracts.Agents.Skills;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Orchestration;

/// <summary>
/// Shared logic for @ and / input triggering and attachment management.
/// Used by ChatInput and EditMessageDialog. Not registered in DI; components instantiate with injected services.
/// </summary>
public class InputOrchestrator
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ISkillsConfigService _skillsConfigService;

    private bool _justSelectedAttachment;
    private List<string> _filePaths = [];
    private List<SkillMetadata> _skills = [];

    public InputOrchestrator(IWorkspaceService workspaceService, ISkillsConfigService skillsConfigService)
    {
        _workspaceService = workspaceService;
        _skillsConfigService = skillsConfigService;
    }

    public string InputText { get; set; } = "";
    public List<AttachmentItem> Attachments { get; } = [];
    public List<string> RequestedSkillIds { get; } = [];
    public bool IsPopoverOpen { get; private set; }
    public string PopoverKind { get; private set; } = "file";
    public string PopoverFilter { get; private set; } = "";
    public IReadOnlyList<string> FilePaths => _filePaths;
    public IReadOnlyList<SkillMetadata> Skills => _skills;

    public event Action? OnStateChanged;

    public async Task HandleInputChangedAsync(string value)
    {
        if (_justSelectedAttachment)
        {
            _justSelectedAttachment = false;
            return;
        }
        InputText = value;

        var lastAt = value.LastIndexOf('@');
        var lastSlash = value.LastIndexOf('/');

        if (lastSlash > lastAt)
        {
            PopoverKind = "skill";
            IsPopoverOpen = true;
            PopoverFilter = lastSlash + 1 < value.Length ? value[(lastSlash + 1)..] : "";
            if (_skills.Count == 0)
                _skills = (await _skillsConfigService.GetMetadataForAgentAsync()).ToList();
        }
        else if (lastAt >= 0)
        {
            PopoverKind = "file";
            IsPopoverOpen = true;
            PopoverFilter = lastAt + 1 < value.Length ? value[(lastAt + 1)..] : "";
            if (_filePaths.Count == 0)
                _filePaths = (await _workspaceService.GetAllowedFilePathsAsync()).ToList();
        }
        else
        {
            IsPopoverOpen = false;
        }

        OnStateChanged?.Invoke();
    }

    public void SelectAttachment(string value)
    {
        if (PopoverKind == "file")
        {
            if (Attachments.OfType<ResolvedPathAttachment>().All(x => x.Path != value))
                Attachments.Add(new ResolvedPathAttachment(value));
            var lastAt = InputText.LastIndexOf('@');
            InputText = lastAt >= 0 ? InputText[..lastAt].TrimEnd() : InputText;
        }
        else
        {
            if (!RequestedSkillIds.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)))
                RequestedSkillIds.Add(value);
            var lastSlash = InputText.LastIndexOf('/');
            InputText = lastSlash >= 0 ? InputText[..lastSlash].TrimEnd() : InputText;
        }

        if (InputText.Length > 0 && !InputText.EndsWith(' '))
            InputText += " ";

        _justSelectedAttachment = true;
        IsPopoverOpen = false;
        OnStateChanged?.Invoke();
    }

    public void RemoveAttachment(AttachmentItem item)
    {
        Attachments.Remove(item);
        OnStateChanged?.Invoke();
    }

    public void RemoveSkill(string skillId)
    {
        RequestedSkillIds.Remove(skillId);
        OnStateChanged?.Invoke();
    }

    public void ClosePopover()
    {
        IsPopoverOpen = false;
        PopoverFilter = "";
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
        IsPopoverOpen = false;
        PopoverFilter = "";
        _filePaths.Clear();
        _skills.Clear();
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
