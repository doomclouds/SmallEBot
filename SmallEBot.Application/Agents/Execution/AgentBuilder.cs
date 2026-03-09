using Microsoft.Extensions.Logging;
using Anthropic;
using Anthropic.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using SmallEBot.Application.Contracts.Agents.Config;
using SmallEBot.Application.Contracts.Agents.Context;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Application.Contracts.Agents.Mcp;
using SmallEBot.Application.Contracts.Agents.Tools;

namespace SmallEBot.Application.Agents.Execution;

/// <summary>Builds and caches AIAgent from context factory and tool factories. MCP connections are managed by IMcpConnectionManager.</summary>
public sealed class AgentBuilder : IAgentBuilder
{
    private readonly IAgentSystemPromptBuilder _systemPromptBuilder;
    private readonly IToolProviderAggregator _toolAggregator;
    private readonly IMcpConnectionManager _mcpConnectionManager;
    private readonly IModelConfigService _modelConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentBuilder> _log;
    private readonly string _skillsPath;
    private readonly string _userSkillsPath;

    private AIAgent? _agent;
    private AITool[]? _allTools;
    private int _contextWindowTokens;

    public AgentBuilder(
        IAgentSystemPromptBuilder systemPromptBuilder,
        IToolProviderAggregator toolAggregator,
        IMcpConnectionManager mcpConnectionManager,
        IModelConfigService modelConfig,
        IVirtualFileSystem vfs,
        IServiceProvider serviceProvider,
        ILogger<AgentBuilder> log)
    {
        _systemPromptBuilder = systemPromptBuilder;
        _toolAggregator = toolAggregator;
        _mcpConnectionManager = mcpConnectionManager;
        _modelConfig = modelConfig;
        _serviceProvider = serviceProvider;
        _log = log;

        var workspaceRoot = vfs.GetRootPath();
        _skillsPath = Path.Combine(workspaceRoot, "sys.skills");
        _userSkillsPath = Path.Combine(workspaceRoot, "skills");
    }

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default)
    {
        if (_agent != null)
            return _agent;

        var instructions = await _systemPromptBuilder.BuildSystemPromptAsync(ct);

        var config = await _modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured. Add a model in Settings.");

        _contextWindowTokens = config.ContextWindow;

        if (_allTools == null)
        {
            var builtIn = await _toolAggregator.GetAllToolsAsync(ct);
            var mcpTools = await _mcpConnectionManager.GetAllToolsAsync(ct);
            var combined = new List<AITool>(builtIn.Length + mcpTools.Length);
            combined.AddRange(builtIn);
            combined.AddRange(mcpTools);
            _allTools = combined.ToArray();
        }

        var apiKey = ResolveApiKey(config.ApiKeySource);
        if (string.IsNullOrEmpty(apiKey))
            _log.LogWarning("API key not set for model '{Model}'. ApiKeySource: {Source}", config.Model, config.ApiKeySource);

        var clientOptions = new ClientOptions { ApiKey = apiKey ?? "", BaseUrl = config.BaseUrl };
        var anthropicClient = new AnthropicClient(clientOptions);

        // Create FileAgentSkillsProvider for skill discovery and loading
#pragma warning disable MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates
        var skillsProvider = new FileAgentSkillsProvider(
            skillPaths: [_skillsPath, _userSkillsPath],
            options: new FileAgentSkillsProviderOptions
            {
                SkillsInstructionPrompt = """
                    You have access to specialized skills.

                    <available_skills>
                    {0}
                    </available_skills>

                    When relevant, use load_skill to load and follow the skill's instructions.
                    """
            });
#pragma warning restore MAAI001

        _agent = anthropicClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SmallEBot",
            ChatOptions = new()
            {
                ModelId = config.Model,
                Instructions = instructions,
                Tools = _allTools
            },
            AIContextProviders = [skillsProvider]
        });
        return _agent;
    }

    public Task InvalidateAsync()
    {
        _agent = null;
        _allTools = null;
        return Task.CompletedTask;
    }

    public async Task<int> GetContextWindowTokensAsync(CancellationToken ct = default)
    {
        // If already cached from agent creation, return it
        if (_contextWindowTokens > 0)
            return _contextWindowTokens;

        // Otherwise, read from config
        var config = await _modelConfig.GetDefaultAsync(ct);
        return config?.ContextWindow ?? 128000;
    }

    public string? GetCachedSystemPromptForTokenCount() => _systemPromptBuilder.GetCachedSystemPrompt();

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
