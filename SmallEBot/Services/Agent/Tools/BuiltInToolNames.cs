namespace SmallEBot.Services.Agent.Tools;

/// <summary>
/// Centralized string constants for built-in tool names.
/// Each constant uses the nameof() self-reference pattern so its identifier and value
/// are always identical at compile time. Reference these constants in system-prompt
/// sections and GetTimeout() switches instead of writing magic strings.
/// </summary>
internal static class BuiltInToolNames
{
    // File tools (FileToolProvider)
    public const string GetWorkspaceRoot = nameof(GetWorkspaceRoot);
    public const string ReadFile         = nameof(ReadFile);
    public const string WriteFile        = nameof(WriteFile);
    public const string AppendFile       = nameof(AppendFile);
    public const string ListFiles        = nameof(ListFiles);
    public const string CopyFile         = nameof(CopyFile);
    public const string CopyDirectory    = nameof(CopyDirectory);

    // Search tools (SearchToolProvider)
    public const string GrepFiles   = nameof(GrepFiles);
    public const string GrepContent = nameof(GrepContent);

    // Shell (ShellToolProvider)
    public const string ExecuteCommand = nameof(ExecuteCommand);

    // Time (TimeToolProvider)
    public const string GetCurrentTime = nameof(GetCurrentTime);

    // Task management (TaskToolProvider)
    public const string ClearTasks    = nameof(ClearTasks);
    public const string SetTaskList   = nameof(SetTaskList);
    public const string ListTasks     = nameof(ListTasks);
    public const string CompleteTask  = nameof(CompleteTask);
    public const string CompleteTasks = nameof(CompleteTasks);

    // Skills (SkillToolProvider)
    public const string ReadSkill      = nameof(ReadSkill);
    public const string ReadSkillFile  = nameof(ReadSkillFile);
    public const string ListSkillFiles = nameof(ListSkillFiles);

    // Conversation analysis (ConversationToolProvider)
    public const string ReadConversationData = nameof(ReadConversationData);

    // Skill generation (SkillGenerationToolProvider)
    public const string GenerateSkill = nameof(GenerateSkill);
}
