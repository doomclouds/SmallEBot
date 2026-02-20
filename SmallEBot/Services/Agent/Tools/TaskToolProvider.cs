using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Conversation;
using SmallEBot.Services.Conversation;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides task list management tools.</summary>
public sealed class TaskToolProvider(
    IConversationTaskContext taskContext,
    ITaskListCache taskCache) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "Task";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ListTasks);
        yield return AIFunctionFactory.Create(SetTaskList);
        yield return AIFunctionFactory.Create(CompleteTask);
        yield return AIFunctionFactory.Create(ClearTasks);
    }

    [Description("List tasks for the current conversation. Returns JSON: { \"tasks\": [ { \"id\", \"title\", \"description\", \"done\" }, ... ] }. Use this to see progress and decide next steps.")]
    private string ListTasks()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: Task list is not available (no conversation context).";
        var data = taskCache.GetOrLoad(conversationId.Value);
        return JsonSerializer.Serialize(new { tasks = data.Tasks }, JsonOptions);
    }

    [Description("Create or replace the task list for the current conversation. Pass tasksJson: a JSON array of objects with \"title\" (required) and optional \"description\". Example: [{\"title\":\"Step 1\",\"description\":\"Brief\"},{\"title\":\"Step 2\"}]. Replaces the entire list; all tasks start as not done. Returns the created list as JSON { \"tasks\": [ ... ] }.")]
    private string SetTaskList(string tasksJson)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: Task list is not available (no conversation context).";
        List<TaskInputItem>? input;
        try
        {
            input = JsonSerializer.Deserialize<List<TaskInputItem>>(tasksJson, JsonOptions);
        }
        catch
        {
            return "Error: Invalid JSON. Pass an array of objects with \"title\" (required) and optional \"description\".";
        }
        if (input == null || input.Count == 0)
            return "Error: tasksJson must be a non-empty array of task objects.";
        var tasks = input
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => new TaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = t.Title!.Trim(),
                Description = t.Description ?? "",
                Done = false
            })
            .ToList();
        if (tasks.Count == 0)
            return "Error: No valid tasks (each must have a non-empty title).";
        taskCache.Update(conversationId.Value, new TaskListData(tasks));
        return JsonSerializer.Serialize(new { tasks }, JsonOptions);
    }

    [Description("Mark a task as done by id. Returns { \"ok\": true, \"task\": { ... }, \"nextTask\": { ... } | null, \"remaining\": N }. nextTask is the next undone task (null if all done) — use nextTask.id directly for the next CompleteTask call without calling ListTasks again. remaining is the count of undone tasks after this completion.")]
    private string CompleteTask(string taskId)
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: Task list is not available (no conversation context).";
        var data = taskCache.GetOrLoad(conversationId.Value);
        var task = data.Tasks.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
        if (task == null)
            return JsonSerializer.Serialize(new { ok = false, error = "Task not found" });
        task.Done = true;
        taskCache.Update(conversationId.Value, data);
        var nextTask = data.Tasks.FirstOrDefault(t => !t.Done);
        var remaining = data.Tasks.Count(t => !t.Done);
        return JsonSerializer.Serialize(new { ok = true, task, nextTask, remaining }, JsonOptions);
    }

    [Description("Clear all tasks for the current conversation. Call this before SetTaskList when starting a new task breakdown to remove old tasks. Returns { \"ok\": true }.")]
    private string ClearTasks()
    {
        var conversationId = taskContext.GetConversationId();
        if (conversationId == null)
            return "Error: Task list is not available (no conversation context).";
        taskCache.Remove(conversationId.Value);
        return JsonSerializer.Serialize(new { ok = true }, JsonOptions);
    }

    private sealed class TaskInputItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
