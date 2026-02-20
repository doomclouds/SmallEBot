using Anthropic;
using Anthropic.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent.Tools;

namespace SmallEBot.Services.Agent;

/// <summary>Builds and caches AIAgent from context factory and tool factories. Owns MCP client disposal on Invalidate.</summary>
public interface IAgentBuilder
{
    Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default);
    Task InvalidateAsync();
    int GetContextWindowTokens();
    /// <summary>Last built system prompt for token estimation; null if not built yet.</summary>
    string? GetCachedSystemPromptForTokenCount();
}

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
