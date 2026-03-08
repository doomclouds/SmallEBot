# SmallEBot Phase 2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement Phase 2 of SmallEBot modular improvements: Model UI Config, MCP Connection Manager, Tool Execution Status UI, and Tool Timeout Optimization.

**Architecture:** Four sequential phases building on each other — P1 adds `IModelConfigService` (singleton) and model selector UI; P2 adds `IMcpConnectionManager` (singleton) replacing per-request MCP creation; P3 enhances `ToolCallStreamUpdate` with phase tracking and adds live status to the chat UI; P4 adds per-tool timeout config and cancellation error handling.

**Tech Stack:** C# 13, .NET 10, Blazor Server, MudBlazor 7, Microsoft.Extensions.AI, Anthropic SDK, ModelContextProtocol.Client

---

## Workflow (MANDATORY for subagent development)

**For each task:**

1. **Implement** the task following the plan steps.
2. **Code review** — Invoke `code-reviewer` subagent to review the implementation against the plan and coding standards.
3. **Quality check** — Run `dotnet build`, fix linter errors, verify behavior; do NOT mark complete until build passes and review is addressed.

Do not skip review or quality checks. Tasks 10+ are complex; strict discipline reduces regressions and integration issues.

---

## Key Files Reference

| Path | Role |
|------|------|
| `SmallEBot.Core/Models/StreamUpdate.cs` | `ToolCallStreamUpdate` record definition |
| `SmallEBot/Services/Agent/AgentBuilder.cs` | Builds/caches `AIAgent`; injects `IMcpToolFactory` + `IConfiguration` |
| `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` | Maps `FunctionCallContent`/`FunctionResultContent` to `ToolCallStreamUpdate` |
| `SmallEBot/Services/Agent/McpToolFactory.cs` | Current per-request MCP connection creator (will be replaced) |
| `SmallEBot/Services/Mcp/McpConfigService.cs` | Reads/writes `.agents/.mcp.json` and `.agents/.sys.mcp.json` |
| `SmallEBot/Services/Agent/Tools/IToolProvider.cs` | Per-tool-provider interface |
| `SmallEBot/Services/Agent/Tools/FileToolProvider.cs` | ReadFile/WriteFile tools |
| `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs` | ExecuteCommand tool |
| `SmallEBot/Components/Chat/ToolCallView.razor` | Existing tool call display (collapsed expansion panel) |
| `SmallEBot/Components/Chat/ChatArea.razor` | Chat UI; `_sendCts` is the cancellation source |
| `SmallEBot/Components/Chat/ChatArea.razor.cs` | `GetStreamingDisplayItems()` — merges `_streamingUpdates` into display items |
| `SmallEBot/Components/Layout/MainLayout.razor` | AppBar with MCP/Skills/Terminal buttons |
| `SmallEBot/Components/Mcp/McpConfigDialog.razor` | MCP configuration dialog |
| `SmallEBot/Extensions/ServiceCollectionExtensions.cs` | DI registration |

## Build & Run Commands

```powershell
# Build
dotnet build SmallEBot/SmallEBot.csproj

# Run
dotnet run --project SmallEBot

# Chain (PowerShell)
dotnet build SmallEBot/SmallEBot.csproj; dotnet run --project SmallEBot
```

---

## Task 1: ModelConfig Data Model and IModelConfigService

**Files:**
- Create: `SmallEBot.Core/Models/ModelConfig.cs`
- Create: `SmallEBot/Services/Agent/IModelConfigService.cs`

**Step 1: Create `ModelConfig` record in Core**

```csharp
// SmallEBot.Core/Models/ModelConfig.cs
namespace SmallEBot.Core.Models;

public record ModelConfig(
    string Id,
    string Name,
    string Provider,        // "anthropic-compatible"
    string BaseUrl,
    string ApiKeySource,    // "env:VAR_NAME" or literal key
    string Model,
    int ContextWindow,
    bool SupportsThinking);
```

**Step 2: Create `IModelConfigService` interface**

```csharp
// SmallEBot/Services/Agent/IModelConfigService.cs
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Agent;

public interface IModelConfigService
{
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);
    Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default);
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
    event Action? OnChanged;
}
```

**Step 3: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```powershell
git add SmallEBot.Core/Models/ModelConfig.cs SmallEBot/Services/Agent/IModelConfigService.cs
git commit -m "feat: add ModelConfig record and IModelConfigService interface"
```

---

## Task 2: ModelConfigService Implementation

**Files:**
- Create: `SmallEBot/Services/Agent/ModelConfigService.cs`

The service persists to `.agents/models.json`. On startup, if the file does not exist, it migrates from `IConfiguration` (Anthropic section). The JSON file structure:

```json
{
  "defaultModelId": "deepseek-reasoner",
  "models": {
    "deepseek-reasoner": {
      "name": "DeepSeek Reasoner",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.deepseek.com/anthropic",
      "apiKeySource": "env:DeepseekKey",
      "model": "deepseek-reasoner",
      "contextWindow": 128000,
      "supportsThinking": true
    }
  }
}
```

**Step 1: Create `ModelConfigService`**

```csharp
// SmallEBot/Services/Agent/ModelConfigService.cs
using System.Text.Json;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Agent;

public sealed class ModelConfigService : IModelConfigService
{
    private readonly string _filePath;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ModelsFile? _cache;

    public event Action? OnChanged;

    public ModelConfigService(IConfiguration config)
    {
        _config = config;
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "models.json");
    }

    public async Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        return file.Models.Values.ToList();
    }

    public async Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        if (file.DefaultModelId != null && file.Models.TryGetValue(file.DefaultModelId, out var m))
            return m;
        return file.Models.Values.FirstOrDefault();
    }

    public async Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        return file.DefaultModelId ?? file.Models.Keys.FirstOrDefault();
    }

    public async Task AddModelAsync(ModelConfig model, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models[model.Id] = model;
            if (file.Models.Count == 1)
                file.DefaultModelId = model.Id;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models[modelId] = model;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task DeleteModelAsync(string modelId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models.Remove(modelId);
            if (file.DefaultModelId == modelId)
                file.DefaultModelId = file.Models.Keys.FirstOrDefault();
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task SetDefaultAsync(string modelId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            if (!file.Models.ContainsKey(modelId))
                throw new InvalidOperationException($"Model '{modelId}' not found");
            file.DefaultModelId = modelId;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    private async Task<ModelsFile> LoadAsync(CancellationToken ct)
    {
        if (_cache != null) return _cache;

        if (File.Exists(_filePath))
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            _cache = JsonSerializer.Deserialize<ModelsFile>(json, JsonOptions) ?? new ModelsFile();
        }
        else
        {
            // Migrate from appsettings.json
            _cache = MigrateFromConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await SaveAsync(_cache, ct);
        }
        return _cache;
    }

    private ModelsFile MigrateFromConfig()
    {
        var baseUrl = _config["Anthropic:BaseUrl"] ?? "https://api.deepseek.com/anthropic";
        var model = _config["Anthropic:Model"] ?? "deepseek-reasoner";
        var contextWindow = _config.GetValue("Anthropic:ContextWindowTokens", 128000);

        // Detect API key source: prefer env vars
        string apiKeySource;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DeepseekKey")))
            apiKeySource = "env:DeepseekKey";
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            apiKeySource = "env:ANTHROPIC_API_KEY";
        else
            apiKeySource = _config["Anthropic:ApiKey"] ?? "";

        var id = SanitizeId(model);
        var config = new ModelConfig(
            Id: id,
            Name: model,
            Provider: "anthropic-compatible",
            BaseUrl: baseUrl,
            ApiKeySource: apiKeySource,
            Model: model,
            ContextWindow: contextWindow,
            SupportsThinking: model.Contains("reasoner", StringComparison.OrdinalIgnoreCase));

        return new ModelsFile
        {
            DefaultModelId = id,
            Models = new Dictionary<string, ModelConfig> { [id] = config }
        };
    }

    private static string SanitizeId(string model) =>
        string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-'));

    private async Task SaveAsync(ModelsFile file, CancellationToken ct)
    {
        _cache = file;
        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ModelsFile
    {
        public string? DefaultModelId { get; set; }
        public Dictionary<string, ModelConfig> Models { get; set; } = [];
    }
}
```

**Step 2: Register in DI (add to `ServiceCollectionExtensions.cs`)**

In `SmallEBot/Extensions/ServiceCollectionExtensions.cs`, add before the return:

```csharp
services.AddSingleton<IModelConfigService, ModelConfigService>();
```

**Step 3: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot/Services/Agent/ModelConfigService.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: implement ModelConfigService with appsettings.json migration"
```

---

## Task 3: Update AgentBuilder to use IModelConfigService

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentBuilder.cs`

Replace `IConfiguration config` dependency with `IModelConfigService modelConfig`. Keep `IConfiguration` only for fallback tokenizer path (not needed; remove entirely since context window now comes from `ModelConfig`).

**Step 1: Modify `AgentBuilder`**

Replace the entire file content:

```csharp
// SmallEBot/Services/Agent/AgentBuilder.cs
using Anthropic;
using Anthropic.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent.Tools;

namespace SmallEBot.Services.Agent;

public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpToolFactory mcpToolFactory,
    IModelConfigService modelConfig,
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    private AIAgent? _agent;
    private List<IAsyncDisposable>? _mcpClients;
    private AITool[]? _allTools;
    private int _contextWindowTokens;

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default)
    {
        var instructions = await contextFactory.BuildSystemPromptAsync(ct);

        if (_agent != null)
            return _agent;

        var config = await modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured. Add a model in Settings.");

        _contextWindowTokens = config.ContextWindow;

        if (_allTools == null)
        {
            var builtIn = await toolAggregator.GetAllToolsAsync(ct);
            var (mcpTools, clients) = await mcpToolFactory.LoadAsync(ct);
            _mcpClients = clients.Count > 0 ? [.. clients] : null;
            var combined = new List<AITool>(builtIn.Length + mcpTools.Length);
            combined.AddRange(builtIn);
            combined.AddRange(mcpTools);
            _allTools = combined.ToArray();
        }

        var apiKey = ResolveApiKey(config.ApiKeySource);
        if (string.IsNullOrEmpty(apiKey))
            log.LogWarning("API key not set for model '{Model}'. ApiKeySource: {Source}", config.Model, config.ApiKeySource);

        var clientOptions = new ClientOptions { ApiKey = apiKey ?? "", BaseUrl = config.BaseUrl };
        var anthropicClient = new AnthropicClient(clientOptions);

        _agent = anthropicClient.AsAIAgent(
            model: config.Model,
            name: "SmallEBot",
            instructions: instructions,
            tools: _allTools);
        return _agent;
    }

    public async Task InvalidateAsync()
    {
        if (_mcpClients != null)
        {
            foreach (var c in _mcpClients)
                await c.DisposeAsync();
            _mcpClients = null;
        }
        _agent = null;
        _allTools = null;
    }

    public int GetContextWindowTokens() => _contextWindowTokens > 0
        ? _contextWindowTokens
        : 128000;

    public string? GetCachedSystemPromptForTokenCount() => contextFactory.GetCachedSystemPrompt();

    private static string? ResolveApiKey(string source)
    {
        if (source.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var varName = source[4..];
            return Environment.GetEnvironmentVariable(varName);
        }
        return string.IsNullOrWhiteSpace(source) ? null : source;
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

Note: If `IConfiguration` was also used as constructor parameter elsewhere in `AgentBuilder`, ensure the old `GetApiKey(config)` call is fully replaced by `ResolveApiKey`.

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/AgentBuilder.cs
git commit -m "feat: AgentBuilder reads model config from IModelConfigService"
```

---

## Task 4: Model Selector UI in AppBar

**Files:**
- Create: `SmallEBot/Components/Agent/ModelSelectorMenu.razor`
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`

**Step 1: Create `ModelSelectorMenu.razor`**

```razor
@* SmallEBot/Components/Agent/ModelSelectorMenu.razor *@
@using SmallEBot.Services.Agent
@inject IModelConfigService ModelConfig
@inject AgentCacheService AgentCache
@implements IDisposable

<MudMenu AnchorOrigin="Origin.BottomRight"
         TransformOrigin="Origin.TopRight"
         Dense="true">
    <ActivatorContent>
        <MudButton Variant="Variant.Text"
                   Color="Color.Default"
                   EndIcon="@Icons.Material.Filled.ArrowDropDown"
                   Size="Size.Small">
            @(_currentModelName ?? "No model")
        </MudButton>
    </ActivatorContent>
    <ChildContent>
        @foreach (var m in _models)
        {
            var modelCopy = m;
            <MudMenuItem OnClick="@(() => SwitchModel(modelCopy.Id))">
                @if (modelCopy.Id == _currentModelId)
                {
                    <MudIcon Icon="@Icons.Material.Filled.Check" Size="Size.Small" Class="mr-1" />
                }
                else
                {
                    <span style="width:20px;display:inline-block"></span>
                }
                @modelCopy.Name
            </MudMenuItem>
        }
        <MudDivider />
        <MudMenuItem OnClick="OpenModelSettings">
            <MudIcon Icon="@Icons.Material.Filled.Settings" Size="Size.Small" Class="mr-1" />
            Manage Models...
        </MudMenuItem>
    </ChildContent>
</MudMenu>

@code {
    [Parameter] public EventCallback OnManageModels { get; set; }

    private IReadOnlyList<Core.Models.ModelConfig> _models = [];
    private string? _currentModelId;
    private string? _currentModelName;

    protected override async Task OnInitializedAsync()
    {
        ModelConfig.OnChanged += OnModelConfigChanged;
        await LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        _models = await ModelConfig.GetAllAsync();
        _currentModelId = await ModelConfig.GetDefaultModelIdAsync();
        _currentModelName = _models.FirstOrDefault(m => m.Id == _currentModelId)?.Name;
        await InvokeAsync(StateHasChanged);
    }

    private async Task SwitchModel(string modelId)
    {
        await ModelConfig.SetDefaultAsync(modelId);
        await AgentCache.InvalidateAgentAsync();
        await LoadModelsAsync();
    }

    private Task OpenModelSettings() => OnManageModels.InvokeAsync();

    private void OnModelConfigChanged() => _ = LoadModelsAsync();

    public void Dispose() => ModelConfig.OnChanged -= OnModelConfigChanged;
}
```

**Step 2: Add model selector to `MainLayout.razor`**

In `SmallEBot/Components/Layout/MainLayout.razor`, add the `@using` and the component to the AppBar.

Add to the `@using` section at the top:
```razor
@using SmallEBot.Components.Agent
```

Add before the `<MudTooltip Text="Theme">` block in the AppBar:
```razor
<ModelSelectorMenu OnManageModels="@OpenModelSettings" />
```

Also add an `OpenModelSettings` method and `@inject IDialogService` if not already present. Add to `@code`:
```csharp
private void OpenModelSettings() =>
    DialogService.ShowAsync<ModelConfigDialog>(string.Empty, new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true });
```

**Step 3: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded (ModelConfigDialog may not exist yet — create a stub if needed)

**Step 4: Commit**

```powershell
git add SmallEBot/Components/Agent/ModelSelectorMenu.razor SmallEBot/Components/Layout/MainLayout.razor
git commit -m "feat: add model selector dropdown to AppBar"
```

---

## Task 5: Model Config Dialog (Settings UI)

**Files:**
- Create: `SmallEBot/Components/Agent/ModelConfigDialog.razor`

**Step 1: Create `ModelConfigDialog.razor`**

```razor
@* SmallEBot/Components/Agent/ModelConfigDialog.razor *@
@using SmallEBot.Core.Models
@using SmallEBot.Services.Agent
@inject IModelConfigService ModelConfig
@inject AgentCacheService AgentCache

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Model Configuration</MudText>
    </TitleContent>
    <DialogContent>
        @if (_models.Count == 0)
        {
            <MudText Color="Color.Secondary">No models configured. Add one below.</MudText>
        }
        @foreach (var model in _models)
        {
            var m = model;
            <MudPaper Class="pa-3 mb-2" Elevation="1">
                <div class="d-flex align-center justify-space-between">
                    <div>
                        <MudText Typo="Typo.subtitle1">
                            🤖 @m.Name
                            @if (m.Id == _defaultModelId)
                            {
                                <MudChip T="string" Size="Size.Small" Color="Color.Primary" Class="ml-2">Default</MudChip>
                            }
                        </MudText>
                        <MudText Typo="Typo.caption" Color="Color.Secondary">
                            @m.BaseUrl • @(m.ContextWindow / 1000)K context
                            @if (m.SupportsThinking) { <span> • Thinking ✓</span> }
                        </MudText>
                        <MudText Typo="Typo.caption" Color="Color.Secondary">Model: @m.Model</MudText>
                    </div>
                    <div class="d-flex gap-1">
                        @if (m.Id != _defaultModelId)
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Outlined" OnClick="@(() => SetDefault(m.Id))">Set Default</MudButton>
                        }
                        <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small" OnClick="@(() => EditModel(m))" />
                        @if (m.Id != _defaultModelId || _models.Count > 1)
                        {
                            <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small" Color="Color.Error" OnClick="@(() => DeleteModel(m.Id))" />
                        }
                    </div>
                </div>
            </MudPaper>
        }

        @if (_editingModel != null)
        {
            <MudDivider Class="my-3" />
            <MudText Typo="Typo.subtitle1" Class="mb-2">@(_isNewModel ? "Add Model" : "Edit Model")</MudText>
            <MudTextField @bind-Value="_editingModel.Name" Label="Name" Required="true" Class="mb-2" />
            <MudTextField @bind-Value="_editingModel.BaseUrl" Label="Base URL" Required="true" Class="mb-2" />
            <MudTextField @bind-Value="_editingModel.ApiKeySource" Label="API Key Source" Required="true" Class="mb-2"
                          HelperText='Use "env:VAR_NAME" to read from environment variable' />
            <MudTextField @bind-Value="_editingModel.Model" Label="Model ID" Required="true" Class="mb-2" />
            <div class="d-flex gap-3 align-center mb-2">
                <MudNumericField @bind-Value="_editingModel.ContextWindow" Label="Context Window" Min="1000" Max="2000000" Class="flex-1" />
                <MudCheckBox @bind-Value="_editingModel.SupportsThinking" Label="Supports Thinking" />
            </div>
            <div class="d-flex gap-2">
                <MudButton OnClick="SaveModel" Color="Color.Primary" Variant="Variant.Filled" Disabled="@(!IsEditValid())">Save</MudButton>
                <MudButton OnClick="@(() => _editingModel = null)">Cancel</MudButton>
            </div>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _editingModel = null)">
            @if (_editingModel == null) { <span>Close</span> } else { <span>Cancel edit</span> }
        </MudButton>
        @if (_editingModel == null)
        {
            <MudButton Color="Color.Primary" Variant="Variant.Filled" OnClick="AddNew">+ Add Model</MudButton>
        }
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance? MudDialog { get; set; }

    private List<ModelConfig> _models = [];
    private string? _defaultModelId;
    private ModelEditState? _editingModel;
    private bool _isNewModel;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _models = (await ModelConfig.GetAllAsync()).ToList();
        _defaultModelId = await ModelConfig.GetDefaultModelIdAsync();
        StateHasChanged();
    }

    private void AddNew()
    {
        _isNewModel = true;
        _editingModel = new ModelEditState();
    }

    private void EditModel(ModelConfig m)
    {
        _isNewModel = false;
        _editingModel = new ModelEditState
        {
            OriginalId = m.Id,
            Name = m.Name,
            BaseUrl = m.BaseUrl,
            ApiKeySource = m.ApiKeySource,
            Model = m.Model,
            ContextWindow = m.ContextWindow,
            SupportsThinking = m.SupportsThinking
        };
    }

    private async Task SaveModel()
    {
        if (_editingModel == null) return;
        var id = _isNewModel
            ? string.Concat(_editingModel.Model.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-'))
            : _editingModel.OriginalId!;
        var config = new ModelConfig(
            Id: id,
            Name: _editingModel.Name,
            Provider: "anthropic-compatible",
            BaseUrl: _editingModel.BaseUrl,
            ApiKeySource: _editingModel.ApiKeySource,
            Model: _editingModel.Model,
            ContextWindow: _editingModel.ContextWindow,
            SupportsThinking: _editingModel.SupportsThinking);

        if (_isNewModel)
            await ModelConfig.AddModelAsync(config);
        else
            await ModelConfig.UpdateModelAsync(id, config);

        await AgentCache.InvalidateAgentAsync();
        _editingModel = null;
        await LoadAsync();
    }

    private async Task SetDefault(string modelId)
    {
        await ModelConfig.SetDefaultAsync(modelId);
        await AgentCache.InvalidateAgentAsync();
        await LoadAsync();
    }

    private async Task DeleteModel(string modelId)
    {
        await ModelConfig.DeleteModelAsync(modelId);
        await AgentCache.InvalidateAgentAsync();
        await LoadAsync();
    }

    private bool IsEditValid() =>
        _editingModel != null &&
        !string.IsNullOrWhiteSpace(_editingModel.Name) &&
        !string.IsNullOrWhiteSpace(_editingModel.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_editingModel.ApiKeySource) &&
        !string.IsNullOrWhiteSpace(_editingModel.Model) &&
        _editingModel.ContextWindow > 0;

    private sealed class ModelEditState
    {
        public string? OriginalId { get; set; }
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string ApiKeySource { get; set; } = "env:ANTHROPIC_API_KEY";
        public string Model { get; set; } = "";
        public int ContextWindow { get; set; } = 128000;
        public bool SupportsThinking { get; set; }
    }
}
```

**Step 2: Build and manually test**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

Manual test:
1. Run `dotnet run --project SmallEBot`
2. Open the app; verify model name appears in AppBar
3. Click the model dropdown; verify it shows current model with checkmark
4. Click "Manage Models..." to open `ModelConfigDialog`
5. Verify current model is listed
6. Add a new test model, verify it appears
7. Delete the test model

**Step 3: Commit**

```powershell
git add SmallEBot/Components/Agent/ModelConfigDialog.razor
git commit -m "feat: add ModelConfigDialog for model CRUD management"
```

---

## Task 6: IMcpConnectionManager Interface and Data Types

**Files:**
- Create: `SmallEBot/Services/Agent/IMcpConnectionManager.cs`

**Step 1: Create the interface file**

```csharp
// SmallEBot/Services/Agent/IMcpConnectionManager.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    Reconnecting
}

public record McpConnectionStatus(
    ConnectionState State,
    DateTime? ConnectedAt,
    DateTime? LastHealthCheck,
    string? LastError,
    int ToolCount);

public record McpConnectionResult(
    bool Success,
    AITool[] Tools,
    string? Error);

public interface IMcpConnectionManager : IAsyncDisposable
{
    /// <summary>Get or create MCP connection, return its tools.</summary>
    Task<McpConnectionResult> GetOrCreateAsync(string serverId, CancellationToken ct = default);

    /// <summary>Get all tools from enabled MCPs (parallel init).</summary>
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);

    /// <summary>Disconnect a specific server.</summary>
    Task DisconnectAsync(string serverId);

    /// <summary>Disconnect all servers.</summary>
    Task DisconnectAllAsync();

    /// <summary>Force health check on a specific server.</summary>
    Task<bool> HealthCheckAsync(string serverId, CancellationToken ct = default);

    /// <summary>Get all connection statuses (for UI).</summary>
    IReadOnlyDictionary<string, McpConnectionStatus> GetAllStatuses();

    /// <summary>Fired when a connection status changes. Args: (serverId, newStatus).</summary>
    event Action<string, McpConnectionStatus>? OnStatusChanged;

    /// <summary>Reconcile pool with current config — add new, remove deleted, reconnect changed.</summary>
    Task ReconcileAsync(CancellationToken ct = default);
}
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 3: Commit**

```powershell
git add SmallEBot/Services/Agent/IMcpConnectionManager.cs
git commit -m "feat: add IMcpConnectionManager interface and connection state types"
```

---

## Task 7: McpConnectionManager Implementation

**Files:**
- Create: `SmallEBot/Services/Agent/McpConnectionManager.cs`

The manager maintains a `Dictionary<string, ConnectionEntry>` keyed by server ID. Each entry holds the `McpClient`, its tools, and current status. A background timer fires every 60 seconds to health-check connected servers.

**Step 1: Create `McpConnectionManager`**

```csharp
// SmallEBot/Services/Agent/McpConnectionManager.cs
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SmallEBot.Services.Mcp;

namespace SmallEBot.Services.Agent;

public sealed class McpConnectionManager(
    IMcpConfigService mcpConfig,
    ILogger<McpConnectionManager> log) : IMcpConnectionManager
{
    private readonly Dictionary<string, ConnectionEntry> _connections = [];
    private readonly Dictionary<string, McpEntrySnapshot> _configSnapshots = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _healthCheckCts = new();
    private bool _healthCheckStarted;

    public event Action<string, McpConnectionStatus>? OnStatusChanged;

    // --- Public interface ---

    public async Task<McpConnectionResult> GetOrCreateAsync(string serverId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_connections.TryGetValue(serverId, out var entry) && entry.Status.State == ConnectionState.Connected)
                return new McpConnectionResult(true, entry.Tools, null);

            return await ConnectServerAsync(serverId, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default)
    {
        var allEntries = await mcpConfig.GetAllAsync(ct);
        var enabledIds = allEntries.Where(e => e.IsEnabled).Select(e => e.Id).ToList();

        EnsureHealthCheckRunning();

        var tasks = enabledIds.Select(id => GetOrCreateAsync(id, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.Success).SelectMany(r => r.Tools).ToArray();
    }

    public async Task DisconnectAsync(string serverId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_connections.TryGetValue(serverId, out var entry))
            {
                await SafeDisposeAsync(entry.Client);
                _connections.Remove(serverId);
                _configSnapshots.Remove(serverId);
                NotifyStatus(serverId, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
        }
        finally { _lock.Release(); }
    }

    public async Task DisconnectAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var (id, entry) in _connections)
            {
                await SafeDisposeAsync(entry.Client);
                NotifyStatus(id, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
            _connections.Clear();
            _configSnapshots.Clear();
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> HealthCheckAsync(string serverId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_connections.TryGetValue(serverId, out var entry))
                return false;

            try
            {
                var tools = await entry.Client.ListToolsAsync(cancellationToken: ct);
                var newStatus = new McpConnectionStatus(
                    ConnectionState.Connected,
                    entry.Status.ConnectedAt,
                    DateTime.UtcNow,
                    null,
                    tools.Count);
                entry.Status = newStatus;
                entry.Tools = tools.ToArray<AITool>();
                NotifyStatus(serverId, newStatus);
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Health check failed for MCP server '{ServerId}'", serverId);
                var errorStatus = new McpConnectionStatus(
                    ConnectionState.Reconnecting,
                    entry.Status.ConnectedAt,
                    DateTime.UtcNow,
                    ex.Message,
                    entry.Tools.Length);
                entry.Status = errorStatus;
                NotifyStatus(serverId, errorStatus);
                _ = Task.Run(() => ReconnectWithBackoffAsync(serverId));
                return false;
            }
        }
        finally { _lock.Release(); }
    }

    public IReadOnlyDictionary<string, McpConnectionStatus> GetAllStatuses()
    {
        return _connections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Status);
    }

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var allEntries = await mcpConfig.GetAllAsync(ct);
        var enabledEntries = allEntries.Where(e => e.IsEnabled).ToList();
        var enabledIds = enabledEntries.Select(e => e.Id).ToHashSet();

        await _lock.WaitAsync(ct);
        try
        {
            // Remove connections no longer in config
            var toRemove = _connections.Keys.Except(enabledIds).ToList();
            foreach (var id in toRemove)
            {
                await SafeDisposeAsync(_connections[id].Client);
                _connections.Remove(id);
                _configSnapshots.Remove(id);
                NotifyStatus(id, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
        }
        finally { _lock.Release(); }

        // Connect/reconnect changed entries (outside lock to allow parallel)
        var connectTasks = enabledEntries
            .Where(e => HasConfigChanged(e))
            .Select(async e =>
            {
                await DisconnectAsync(e.Id);
                await GetOrCreateAsync(e.Id, ct);
            });
        await Task.WhenAll(connectTasks);
    }

    // --- Private implementation ---

    private async Task<McpConnectionResult> ConnectServerAsync(string serverId, CancellationToken ct)
    {
        // Update status to Connecting (called within _lock)
        NotifyStatus(serverId, new McpConnectionStatus(ConnectionState.Connecting, null, null, null, 0));

        var allEntries = await mcpConfig.GetAllAsync(ct);
        var entryWithSource = allEntries.FirstOrDefault(e => e.Id == serverId);
        if (entryWithSource == default)
        {
            var notFound = new McpConnectionStatus(ConnectionState.Error, null, null, "Server not found in config", 0);
            NotifyStatus(serverId, notFound);
            return new McpConnectionResult(false, [], "Server not found in config");
        }

        var entry = entryWithSource.Entry;
        try
        {
            IAsyncDisposable client;
            IList<AITool> tools;

            var isStdio = "stdio".Equals(entry.Type, StringComparison.OrdinalIgnoreCase)
                          || (string.IsNullOrEmpty(entry.Type) && !string.IsNullOrEmpty(entry.Command));

            if (isStdio)
            {
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = serverId,
                    Command = entry.Command!,
                    Arguments = entry.Args ?? [],
                    EnvironmentVariables = entry.Env ?? new Dictionary<string, string?>()
                });
                var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                client = mcpClient;
                tools = await mcpClient.ListToolsAsync(cancellationToken: ct);
            }
            else
            {
                var options = new HttpClientTransportOptions
                {
                    Endpoint = new Uri(entry.Url!),
                    TransportMode = HttpTransportMode.AutoDetect,
                    ConnectionTimeout = TimeSpan.FromSeconds(30)
                };
                HttpClientTransport transport;
                if (entry.Headers is { Count: > 0 })
                {
                    var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                    foreach (var h in entry.Headers)
                    {
                        if (!string.IsNullOrEmpty(h.Key))
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value ?? "");
                    }
                    transport = new HttpClientTransport(options, httpClient, ownsHttpClient: true);
                }
                else
                {
                    transport = new HttpClientTransport(options);
                }
                var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                client = mcpClient;
                tools = await ((IMcpClient)mcpClient).ListToolsAsync(cancellationToken: ct);
            }

            var connected = new McpConnectionStatus(
                ConnectionState.Connected, DateTime.UtcNow, DateTime.UtcNow, null, tools.Count);
            _connections[serverId] = new ConnectionEntry(client, tools.ToArray<AITool>(), connected);
            _configSnapshots[serverId] = new McpEntrySnapshot(entry.Command, entry.Url, entry.Args);
            NotifyStatus(serverId, connected);
            log.LogInformation("MCP server '{ServerId}' connected with {Count} tools.", serverId, tools.Count);
            return new McpConnectionResult(true, tools.ToArray<AITool>(), null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to connect MCP server '{ServerId}'.", serverId);
            var error = new McpConnectionStatus(ConnectionState.Error, null, null, ex.Message, 0);
            NotifyStatus(serverId, error);
            return new McpConnectionResult(false, [], ex.Message);
        }
    }

    private async Task ReconnectWithBackoffAsync(string serverId)
    {
        var delays = new[] { 5, 10, 20, 60 };
        foreach (var delaySecs in delays)
        {
            if (_healthCheckCts.IsCancellationRequested) return;
            await Task.Delay(TimeSpan.FromSeconds(delaySecs), _healthCheckCts.Token).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _lock.WaitAsync(cts.Token);
            try
            {
                var result = await ConnectServerAsync(serverId, cts.Token);
                if (result.Success) return;
            }
            catch { /* continue backoff */ }
            finally { _lock.Release(); }
        }

        // Give up after all retries
        await _lock.WaitAsync();
        try
        {
            if (_connections.TryGetValue(serverId, out var entry))
            {
                var disconnected = new McpConnectionStatus(
                    ConnectionState.Disconnected, null, null, "Max retries exceeded", 0);
                entry.Status = disconnected;
                NotifyStatus(serverId, disconnected);
            }
        }
        finally { _lock.Release(); }
    }

    private void EnsureHealthCheckRunning()
    {
        if (_healthCheckStarted) return;
        _healthCheckStarted = true;
        _ = Task.Run(HealthCheckLoopAsync);
    }

    private async Task HealthCheckLoopAsync()
    {
        while (!_healthCheckCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), _healthCheckCts.Token).ConfigureAwait(false);
            var connectedIds = _connections
                .Where(kvp => kvp.Value.Status.State == ConnectionState.Connected)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var id in connectedIds)
            {
                if (_healthCheckCts.IsCancellationRequested) break;
                try { await HealthCheckAsync(id); } catch { /* keep going */ }
            }
        }
    }

    private bool HasConfigChanged(McpEntryWithSource e)
    {
        if (!_configSnapshots.TryGetValue(e.Id, out var snap)) return true;
        return snap.Command != e.Entry.Command
            || snap.Url != e.Entry.Url
            || !ArgsEqual(snap.Args, e.Entry.Args);
    }

    private static bool ArgsEqual(string[]? a, string[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SequenceEqual(b);
    }

    private void NotifyStatus(string serverId, McpConnectionStatus status) =>
        OnStatusChanged?.Invoke(serverId, status);

    private static async Task SafeDisposeAsync(IAsyncDisposable? disposable)
    {
        if (disposable == null) return;
        try { await disposable.DisposeAsync(); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _healthCheckCts.CancelAsync();
        _healthCheckCts.Dispose();
        await DisconnectAllAsync();
        _lock.Dispose();
    }

    // --- Inner types ---
    private sealed class ConnectionEntry(IAsyncDisposable client, AITool[] tools, McpConnectionStatus status)
    {
        public IAsyncDisposable Client { get; } = client;
        public AITool[] Tools { get; set; } = tools;
        public McpConnectionStatus Status { get; set; } = status;
    }

    private sealed record McpEntrySnapshot(string? Command, string? Url, string[]? Args);
}
```

> **Note on `McpEntryWithSource`:** Check the actual type returned by `mcpConfig.GetAllAsync()` in `McpConfigService.cs`. It returns `IReadOnlyList<McpEntryWithSource>` where each has `Id`, `Entry`, `IsEnabled`, `Source`. Adjust the implementation to match the actual type.

**Step 2: Register as Singleton in DI**

In `ServiceCollectionExtensions.cs`, add:
```csharp
services.AddSingleton<IMcpConnectionManager, McpConnectionManager>();
```

Also keep `IMcpToolFactory` registration for now — we will remove it in the next task.

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

If type mismatches occur with `McpEntryWithSource`, read `SmallEBot/Services/Mcp/McpConfigService.cs` to find the exact returned type shape and adjust.

**Step 4: Commit**

```powershell
git add SmallEBot/Services/Agent/McpConnectionManager.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: implement McpConnectionManager with health check and auto-reconnect"
```

---

## Task 8: Wire AgentBuilder to IMcpConnectionManager

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentBuilder.cs`

**Step 1: Replace `IMcpToolFactory` with `IMcpConnectionManager` in `AgentBuilder`**

Change the constructor to accept `IMcpConnectionManager` instead of `IMcpToolFactory`. Remove `_mcpClients` field. In `GetOrCreateAgentAsync`, replace the `mcpToolFactory.LoadAsync()` call with `mcpConnectionManager.GetAllToolsAsync()`. In `InvalidateAsync`, no longer dispose MCP clients (manager owns connections).

```csharp
public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpConnectionManager mcpConnectionManager,   // <-- was IMcpToolFactory
    IModelConfigService modelConfig,
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    private AIAgent? _agent;
    private AITool[]? _allTools;
    private int _contextWindowTokens;

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default)
    {
        var instructions = await contextFactory.BuildSystemPromptAsync(ct);

        if (_agent != null)
            return _agent;

        var config = await modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured. Add a model in Settings.");

        _contextWindowTokens = config.ContextWindow;

        if (_allTools == null)
        {
            var builtIn = await toolAggregator.GetAllToolsAsync(ct);
            var mcpTools = await mcpConnectionManager.GetAllToolsAsync(ct);
            _allTools = [..builtIn, ..mcpTools];
        }

        var apiKey = ResolveApiKey(config.ApiKeySource);
        if (string.IsNullOrEmpty(apiKey))
            log.LogWarning("API key not set for model '{Model}'.", config.Model);

        var clientOptions = new ClientOptions { ApiKey = apiKey ?? "", BaseUrl = config.BaseUrl };
        var anthropicClient = new AnthropicClient(clientOptions);

        _agent = anthropicClient.AsAIAgent(
            model: config.Model,
            name: "SmallEBot",
            instructions: instructions,
            tools: _allTools);
        return _agent;
    }

    public Task InvalidateAsync()
    {
        // MCP connections are managed by IMcpConnectionManager (singleton); do not dispose here.
        _agent = null;
        _allTools = null;
        return Task.CompletedTask;
    }

    public int GetContextWindowTokens() => _contextWindowTokens > 0 ? _contextWindowTokens : 128000;

    public string? GetCachedSystemPromptForTokenCount() => contextFactory.GetCachedSystemPrompt();

    private static string? ResolveApiKey(string source)
    {
        if (source.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(source[4..]);
        return string.IsNullOrWhiteSpace(source) ? null : source;
    }
}
```

**Step 2: Update DI registration**

In `ServiceCollectionExtensions.cs`:
- Remove: `services.AddScoped<IMcpToolFactory, McpToolFactory>();`
- The `McpToolFactory` class can remain but is no longer registered. `IMcpConnectionManager` is already registered.

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded, 0 errors

**Step 4: Commit**

```powershell
git add SmallEBot/Services/Agent/AgentBuilder.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: AgentBuilder uses IMcpConnectionManager instead of IMcpToolFactory"
```

---

## Task 9: MCP Connection Status in McpConfigDialog

**Files:**
- Modify: `SmallEBot/Components/Mcp/McpConfigDialog.razor`

Read the existing `McpConfigDialog.razor` first to understand its current structure. Then add connection status indicators next to each server entry.

**Step 1: Read the existing dialog**

Read `SmallEBot/Components/Mcp/McpConfigDialog.razor` fully.

**Step 2: Inject `IMcpConnectionManager` and display status**

Add to the dialog's injections:
```razor
@inject IMcpConnectionManager McpConnectionManager
@implements IDisposable
```

Add a helper method:
```csharp
private string GetStatusIcon(string serverId)
{
    var statuses = McpConnectionManager.GetAllStatuses();
    if (!statuses.TryGetValue(serverId, out var status))
        return "⚪";
    return status.State switch
    {
        ConnectionState.Connected => "🟢",
        ConnectionState.Connecting => "🟡",
        ConnectionState.Reconnecting => "🟠",
        ConnectionState.Error => "🔴",
        _ => "⚪"
    };
}

private string GetStatusText(string serverId)
{
    var statuses = McpConnectionManager.GetAllStatuses();
    if (!statuses.TryGetValue(serverId, out var status))
        return "Not connected";
    return status.State switch
    {
        ConnectionState.Connected => $"Connected • {status.ToolCount} tools",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Reconnecting => "Reconnecting...",
        ConnectionState.Error => $"Error: {status.LastError}",
        ConnectionState.Disconnected => "Disconnected",
        _ => ""
    };
}
```

In the `OnInitialized` / `OnInitializedAsync`:
```csharp
McpConnectionManager.OnStatusChanged += OnStatusChanged;
```

```csharp
private void OnStatusChanged(string id, McpConnectionStatus status) =>
    InvokeAsync(StateHasChanged);

public void Dispose() => McpConnectionManager.OnStatusChanged -= OnStatusChanged;
```

In the Razor markup for each server card, add status display:
```razor
<MudText Typo="Typo.caption">
    @GetStatusIcon(serverId) @GetStatusText(serverId)
</MudText>
```

Also add a "Retry" button for Error state:
```razor
@if (McpConnectionManager.GetAllStatuses().TryGetValue(serverId, out var s) && s.State == ConnectionState.Error)
{
    <MudButton Size="Size.Small" OnClick="@(() => RetryConnect(serverId))">Retry</MudButton>
}
```

```csharp
private async Task RetryConnect(string serverId)
{
    await McpConnectionManager.GetOrCreateAsync(serverId);
    StateHasChanged();
}
```

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 4: Commit**

```powershell
git add SmallEBot/Components/Mcp/McpConfigDialog.razor
git commit -m "feat: show MCP connection status in MCP config dialog"
```

---

## Task 10: Enhance ToolCallStreamUpdate with Phase Tracking

**Files:**
- Modify: `SmallEBot.Core/Models/StreamUpdate.cs`

**Step 1: Add `ToolCallPhase` enum and update `ToolCallStreamUpdate`**

```csharp
// SmallEBot.Core/Models/StreamUpdate.cs
namespace SmallEBot.Core.Models;

public abstract record StreamUpdate;

public sealed record TextStreamUpdate(string Text) : StreamUpdate;

public sealed record ThinkStreamUpdate(string Text) : StreamUpdate;

public enum ToolCallPhase
{
    Started,      // Tool call initiated, args streaming from LLM
    ArgsReceived, // All arguments received, about to execute
    Executing,    // Tool function is running
    Completed,    // Execution succeeded
    Failed,       // Execution failed
    Cancelled     // Cancelled by user
}

/// <summary>
/// Represents tool call progress events during streaming.
/// CallId links Started/ArgsReceived/Executing/Completed events for the same call.
/// </summary>
public sealed record ToolCallStreamUpdate(
    string ToolName,
    string? CallId = null,
    ToolCallPhase Phase = ToolCallPhase.Started,
    string? Arguments = null,
    string? Result = null,
    TimeSpan? Elapsed = null,
    string? Error = null) : StreamUpdate;
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build errors on callers of `ToolCallStreamUpdate` — expected; fix in next steps.

**Step 3: Fix compile errors in `AgentRunnerAdapter.cs`**

The current code has:
```csharp
case FunctionCallContent fnCall:
    yield return new ToolCallStreamUpdate(fnCall.Name, ToJsonString(fnCall.Arguments));
case FunctionResultContent fnResult:
    yield return new ToolCallStreamUpdate(fnResult.CallId, Result: ToJsonString(fnResult.Result));
```

Update to:
```csharp
case FunctionCallContent fnCall:
    var sw = Stopwatch.StartNew();
    toolTimers[fnCall.CallId] = sw;
    yield return new ToolCallStreamUpdate(
        ToolName: fnCall.Name,
        CallId: fnCall.CallId,
        Phase: ToolCallPhase.Started,
        Arguments: ToJsonString(fnCall.Arguments),
        Elapsed: TimeSpan.Zero);
    break;

case FunctionResultContent fnResult:
    if (toolTimers.TryGetValue(fnResult.CallId, out var timer))
    {
        timer.Stop();
        yield return new ToolCallStreamUpdate(
            ToolName: fnResult.CallId,
            CallId: fnResult.CallId,
            Phase: ToolCallPhase.Completed,
            Result: ToJsonString(fnResult.Result),
            Elapsed: timer.Elapsed);
        toolTimers.Remove(fnResult.CallId);
    }
    break;
```

Add `var toolTimers = new Dictionary<string, Stopwatch>();` before the `await foreach` loop. Add `using System.Diagnostics;` at the top.

> **Important:** `FunctionCallContent.Arguments` is already the full args string (not streaming per-token). The `Started` phase represents when the function call is first seen in the stream. The `Completed` phase fires when `FunctionResultContent` arrives (after execution). This gives elapsed = time from call initiation to result.

**Step 4: Fix compile errors in `ChatArea.razor.cs`**

The `GetStreamingDisplayItems()` method checks:
```csharp
if (tc.Result != null && tc.Arguments == null)
```

Update this logic to use `Phase`:
```csharp
if (update is ToolCallStreamUpdate tc)
{
    if (tc.Phase == ToolCallPhase.Completed || tc.Phase == ToolCallPhase.Failed || tc.Phase == ToolCallPhase.Cancelled)
    {
        // Result update: find the matching tool call by CallId and update it
        var lastReplyTool = replyItems.LastOrDefault(x => x.IsReplyTool && x.ToolCallId == tc.CallId);
        if (lastReplyTool != null)
        {
            lastReplyTool.ToolResult = tc.Result;
            lastReplyTool.Phase = tc.Phase;
            lastReplyTool.Elapsed = tc.Elapsed;
        }
        else
        {
            var lastReasoningTool = reasoningSteps.LastOrDefault(x => !x.IsThink && x.ToolCallId == tc.CallId);
            if (lastReasoningTool != null)
            {
                lastReasoningTool.ToolResult = tc.Result;
                lastReasoningTool.Phase = tc.Phase;
                lastReasoningTool.Elapsed = tc.Elapsed;
            }
        }
        continue;
    }
    // New tool call (Started or ArgsReceived)
    if (string.IsNullOrEmpty(tc.ToolName) && tc.CallId == null)
        continue;
    var toolItem = new StreamDisplayItem
    {
        IsReplyTool = true,
        ToolCallId = tc.CallId,
        ToolName = tc.ToolName,
        ToolArguments = tc.Arguments,
        Phase = tc.Phase,
        Elapsed = tc.Elapsed
    };
    if (seenText)
        replyItems.Add(toolItem);
    else
        reasoningSteps.Add(new ReasoningStep
        {
            IsThink = false,
            ToolCallId = tc.CallId,
            ToolName = tc.ToolName,
            ToolArguments = tc.Arguments,
            Phase = tc.Phase,
            Elapsed = tc.Elapsed
        });
}
```

Add `ToolCallId`, `Phase`, `Elapsed` fields to `StreamDisplayItem` and `ReasoningStep` in `ChatArea.razor.cs`. Update `GetStreamingDisplayItemViews()` to pass these through to `StreamingDisplayItemView`.

**Step 5: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 6: Commit**

```powershell
git add SmallEBot.Core/Models/StreamUpdate.cs SmallEBot/Services/Agent/AgentRunnerAdapter.cs SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat: enhance ToolCallStreamUpdate with Phase, CallId, Elapsed tracking"
```

---

## Task 11: ToolCallView Enhancement with Status Display

**Files:**
- Modify: `SmallEBot/Components/Chat/ToolCallView.razor`

**Step 1: Rewrite `ToolCallView.razor` to show phase, elapsed, and cancel button**

The existing component is a simple expansion panel. Replace it with a status-aware component:

```razor
@* SmallEBot/Components/Chat/ToolCallView.razor *@
@using SmallEBot.Core.Models

@if (ShowToolCalls)
{
    <div class="@WrapperClass tool-call-view">
        <MudPaper Class="@PaperClass" Elevation="0">
            <div class="d-flex align-center gap-2 pa-2">
                <MudIcon Icon="@PhaseIcon" Size="Size.Small" Color="@PhaseColor" />
                <span class="mud-typography-body2 font-weight-medium">@ToolName</span>
                <span class="mud-typography-caption mud-secondary-text">@StatusText</span>
                @if (Elapsed.HasValue)
                {
                    <span class="mud-typography-caption mud-secondary-text ml-auto">@FormatElapsed(Elapsed.Value)</span>
                }
                @if (CanCancel && OnCancel.HasDelegate)
                {
                    <MudIconButton Icon="@Icons.Material.Filled.Cancel"
                                   Size="Size.Small"
                                   Color="Color.Warning"
                                   OnClick="OnCancel"
                                   title="Cancel" />
                }
                else if (HasDetails)
                {
                    <MudIconButton Icon="@(_expanded ? Icons.Material.Filled.ExpandLess : Icons.Material.Filled.ExpandMore)"
                                   Size="Size.Small"
                                   OnClick="@(() => _expanded = !_expanded)"
                                   Class="ml-auto" />
                }
            </div>
            @if (_expanded && HasDetails)
            {
                <MudDivider />
                <div class="pa-2">
                    @if (!string.IsNullOrEmpty(ToolArguments))
                    {
                        <MudText Typo="Typo.caption" Color="Color.Secondary">Arguments:</MudText>
                        <pre class="tool-call-pre">@ToolArguments</pre>
                    }
                    @if (!string.IsNullOrEmpty(ToolResult))
                    {
                        <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mt-1">Result:</MudText>
                        <pre class="tool-call-pre">@ToolResult</pre>
                    }
                    @if (!string.IsNullOrEmpty(ErrorMessage))
                    {
                        <MudText Typo="Typo.caption" Color="Color.Error" Class="mt-1">Error: @ErrorMessage</MudText>
                    }
                </div>
            }
        </MudPaper>
    </div>
}

@code {
    [Parameter] public string? ToolName { get; set; }
    [Parameter] public string? ToolArguments { get; set; }
    [Parameter] public string? ToolResult { get; set; }
    [Parameter] public string? ErrorMessage { get; set; }
    [Parameter] public ToolCallPhase Phase { get; set; } = ToolCallPhase.Completed;
    [Parameter] public TimeSpan? Elapsed { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public string WrapperClass { get; set; } = "mt-2";
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool _expanded;

    private bool HasDetails => !string.IsNullOrEmpty(ToolArguments)
                            || !string.IsNullOrEmpty(ToolResult)
                            || !string.IsNullOrEmpty(ErrorMessage);

    private bool CanCancel => Phase is ToolCallPhase.Started
                           or ToolCallPhase.ArgsReceived
                           or ToolCallPhase.Executing;

    private string PaperClass => Phase switch
    {
        ToolCallPhase.Failed => "tool-call-paper tool-call-paper-error",
        ToolCallPhase.Cancelled => "tool-call-paper tool-call-paper-cancelled",
        ToolCallPhase.Completed => "tool-call-paper tool-call-paper-done",
        _ => "tool-call-paper tool-call-paper-active"
    };

    private string PhaseIcon => Phase switch
    {
        ToolCallPhase.Started => Icons.Material.Filled.HourglassTop,
        ToolCallPhase.ArgsReceived => Icons.Material.Filled.HourglassBottom,
        ToolCallPhase.Executing => Icons.Material.Filled.Settings,
        ToolCallPhase.Completed => Icons.Material.Filled.CheckCircle,
        ToolCallPhase.Failed => Icons.Material.Filled.Error,
        ToolCallPhase.Cancelled => Icons.Material.Filled.Cancel,
        _ => Icons.Material.Filled.Build
    };

    private Color PhaseColor => Phase switch
    {
        ToolCallPhase.Completed => Color.Success,
        ToolCallPhase.Failed => Color.Error,
        ToolCallPhase.Cancelled => Color.Warning,
        _ => Color.Default
    };

    private string StatusText => Phase switch
    {
        ToolCallPhase.Started => "Receiving args...",
        ToolCallPhase.ArgsReceived => "Args received",
        ToolCallPhase.Executing => "Executing...",
        ToolCallPhase.Completed => "Completed",
        ToolCallPhase.Failed => "Failed",
        ToolCallPhase.Cancelled => "Cancelled",
        _ => ""
    };

    private static string FormatElapsed(TimeSpan e)
    {
        if (e.TotalMinutes >= 1) return $"{(int)e.TotalMinutes}m {e.Seconds}s";
        if (e.TotalSeconds >= 1) return $"{e.TotalSeconds:F1}s";
        return $"{e.TotalMilliseconds:F0}ms";
    }
}
```

**Step 2: Update callers to pass Phase and Elapsed**

In `ChatArea.razor`, the `<ToolCallView>` invocation for persisted tool calls (from `item.ToolCall`):
```razor
<ToolCallView ToolName="@tc.ToolName"
              ToolArguments="@tc.Arguments"
              ToolResult="@tc.Result"
              Phase="ToolCallPhase.Completed"
              ShowToolCalls="@ShowToolCalls" />
```

For streaming tool calls (from `StreamDisplayItem`), update `StreamingMessageView` to pass phase/elapsed. Inspect `StreamingMessageView.razor` to find where `ToolCallView` is rendered and add the parameters.

**Step 3: Add cancel button wiring in `ChatArea.razor`**

For streaming tool calls that are still in-progress (phase = Started/ArgsReceived/Executing), pass `OnCancel="@StopSend"` to `ToolCallView` so the cancel button calls `_sendCts?.Cancel()`.

In the streaming tool call section:
```razor
<ToolCallView ToolName="@item.ToolName"
              ToolArguments="@item.ToolArguments"
              ToolResult="@item.ToolResult"
              Phase="@item.Phase"
              Elapsed="@item.Elapsed"
              ShowToolCalls="@ShowToolCalls"
              OnCancel="@(item.Phase != ToolCallPhase.Completed ? EventCallback.Factory.Create(this, StopSend) : EventCallback.Empty)" />
```

**Step 4: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 5: Commit**

```powershell
git add SmallEBot/Components/Chat/ToolCallView.razor SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat: ToolCallView shows phase status, elapsed time, and cancel button"
```

---

## Task 12: Per-Tool Timeout Support

**Files:**
- Modify: `SmallEBot/Services/Agent/Tools/IToolProvider.cs`
- Modify: `SmallEBot/Services/Agent/Tools/FileToolProvider.cs`
- Modify: `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs`

**Step 1: Add `GetTimeout` to `IToolProvider`**

Read `SmallEBot/Services/Agent/Tools/IToolProvider.cs` first to see the current interface. Add:

```csharp
/// <summary>Returns the timeout for a specific tool, or null to use the default.</summary>
TimeSpan? GetTimeout(string toolName) => null;
```

(Default interface implementation so existing providers don't need to change.)

**Step 2: Override in `FileToolProvider.cs`**

```csharp
public TimeSpan? GetTimeout(string toolName) => toolName switch
{
    "WriteFile" => TimeSpan.FromMinutes(10),
    "ReadFile"  => TimeSpan.FromSeconds(30),
    "ListFiles" => TimeSpan.FromSeconds(30),
    "GrepFiles" => TimeSpan.FromSeconds(60),
    "GrepContent" => TimeSpan.FromSeconds(60),
    _ => null
};
```

**Step 3: Override in `ShellToolProvider.cs`**

```csharp
public TimeSpan? GetTimeout(string toolName) => toolName switch
{
    "ExecuteCommand" => TimeSpan.FromMinutes(10),
    _ => null
};
```

**Step 4: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`
Expected: Build succeeded

**Step 5: Commit**

```powershell
git add SmallEBot/Services/Agent/Tools/IToolProvider.cs SmallEBot/Services/Agent/Tools/FileToolProvider.cs SmallEBot/Services/Agent/Tools/ShellToolProvider.cs
git commit -m "feat: add GetTimeout to IToolProvider; set file and shell tool timeouts"
```

---

## Task 13: Final Integration Test

**Step 1: Run the application**

```powershell
dotnet run --project SmallEBot
```

**Step 2: Manual verification checklist**

Model UI (P1):
- [ ] AppBar shows current model name with dropdown arrow
- [ ] Clicking dropdown shows all configured models with checkmark on current
- [ ] "Manage Models..." opens the `ModelConfigDialog`
- [ ] Can add a new model (fill form, save)
- [ ] Can edit an existing model
- [ ] Can delete a non-default model
- [ ] Can switch default model; AppBar updates
- [ ] After switching, agent uses new model (check log output)
- [ ] After restart, model persists from `models.json`

MCP Connection Manager (P2):
- [ ] Open MCP Config dialog; each server shows status icon (🟢/🟡/🔴/⚪)
- [ ] Status text shows "Connected • N tools" for connected servers
- [ ] Error state shows error message and Retry button
- [ ] Clicking Retry reconnects
- [ ] Re-opening dialog after agent invalidation does NOT recreate all MCP connections

Tool Status UI (P3):
- [ ] During streaming, tool calls show "Receiving args..." with ⏳ icon
- [ ] After tool executes, shows "Completed" with ✅ and elapsed time
- [ ] In-progress tool calls show [Cancel] button
- [ ] Clicking Cancel stops the streaming
- [ ] Failed tool calls show ❌ with error message
- [ ] Completed tool calls can be expanded to show args and result

Per-Tool Timeout (P4):
- [ ] Large WriteFile (1000+ lines) does not timeout prematurely
- [ ] Long-running ExecuteCommand does not timeout at 30s default

**Step 3: Final commit**

```powershell
git add -A
git commit -m "feat: Phase 2 complete — Model UI, MCP connection manager, tool status UI, timeouts"
```

---

## Summary: Files Created/Modified

| File | Action |
|------|--------|
| `SmallEBot.Core/Models/ModelConfig.cs` | Create |
| `SmallEBot.Core/Models/StreamUpdate.cs` | Modify — add `ToolCallPhase`, enhance `ToolCallStreamUpdate` |
| `SmallEBot/Services/Agent/IModelConfigService.cs` | Create |
| `SmallEBot/Services/Agent/ModelConfigService.cs` | Create |
| `SmallEBot/Services/Agent/IMcpConnectionManager.cs` | Create |
| `SmallEBot/Services/Agent/McpConnectionManager.cs` | Create |
| `SmallEBot/Services/Agent/AgentBuilder.cs` | Modify — use `IModelConfigService` + `IMcpConnectionManager` |
| `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` | Modify — emit `ToolCallPhase` updates with `Stopwatch` |
| `SmallEBot/Services/Agent/Tools/IToolProvider.cs` | Modify — add `GetTimeout` default method |
| `SmallEBot/Services/Agent/Tools/FileToolProvider.cs` | Modify — implement `GetTimeout` |
| `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs` | Modify — implement `GetTimeout` |
| `SmallEBot/Components/Agent/ModelSelectorMenu.razor` | Create |
| `SmallEBot/Components/Agent/ModelConfigDialog.razor` | Create |
| `SmallEBot/Components/Chat/ToolCallView.razor` | Modify — status/elapsed/cancel |
| `SmallEBot/Components/Chat/ChatArea.razor` | Modify — pass phase/elapsed/cancel to ToolCallView |
| `SmallEBot/Components/Chat/ChatArea.razor.cs` | Modify — propagate Phase/Elapsed through display items |
| `SmallEBot/Components/Layout/MainLayout.razor` | Modify — add `ModelSelectorMenu` to AppBar |
| `SmallEBot/Components/Mcp/McpConfigDialog.razor` | Modify — show connection status |
| `SmallEBot/Extensions/ServiceCollectionExtensions.cs` | Modify — register new services |
