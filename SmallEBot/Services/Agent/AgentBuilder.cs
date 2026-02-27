using Anthropic;
using Anthropic.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent.Tools;

namespace SmallEBot.Services.Agent;

/// <summary>Builds and caches AIAgent from context factory and tool factories. MCP connections are managed by IMcpConnectionManager.</summary>
public interface IAgentBuilder
{
    Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default);
    Task InvalidateAsync();
    Task<int> GetContextWindowTokensAsync(CancellationToken ct = default);
    /// <summary>Last built system prompt for token estimation; null if not built yet.</summary>
    string? GetCachedSystemPromptForTokenCount();
}

public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpConnectionManager mcpConnectionManager,
    IModelConfigService modelConfig,
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    private AIAgent? _agent;
    private AITool[]? _allTools;
    private int _contextWindowTokens;

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default)
    {
        if (_agent != null)
            return _agent;

        var instructions = await contextFactory.BuildSystemPromptAsync(ct);

        var config = await modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured. Add a model in Settings.");

        _contextWindowTokens = config.ContextWindow;

        if (_allTools == null)
        {
            var builtIn = await toolAggregator.GetAllToolsAsync(ct);
            var mcpTools = await mcpConnectionManager.GetAllToolsAsync(ct);
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
        _agent = null;
        _allTools = null;
    }

    public async Task<int> GetContextWindowTokensAsync(CancellationToken ct = default)
    {
        // If already cached from agent creation, return it
        if (_contextWindowTokens > 0)
            return _contextWindowTokens;

        // Otherwise, read from config
        var config = await modelConfig.GetDefaultAsync(ct);
        return config?.ContextWindow ?? 128000;
    }

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
