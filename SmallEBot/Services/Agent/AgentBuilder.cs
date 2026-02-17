using Anthropic;
using Anthropic.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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
    IBuiltInToolFactory builtInToolFactory,
    IMcpToolFactory mcpToolFactory,
    IConfiguration config,
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    private AIAgent? _agent;
    private List<IAsyncDisposable>? _mcpClients;
    private AITool[]? _allTools;
    private readonly int _contextWindowTokens = config.GetValue("DeepSeek:ContextWindowTokens", 128000);

    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default)
    {
        var instructions = await contextFactory.BuildSystemPromptAsync(ct);

        if (_agent != null)
            return _agent;

        if (_allTools == null)
        {
            var builtIn = builtInToolFactory.CreateTools();
            var (mcpTools, clients) = await mcpToolFactory.LoadAsync(ct);
            _mcpClients = clients.Count > 0 ? [.. clients] : null;
            var combined = new List<AITool>(builtIn.Length + mcpTools.Length);
            combined.AddRange(builtIn);
            combined.AddRange(mcpTools);
            _allTools = combined.ToArray();
        }

        var apiKey = config["Anthropic:ApiKey"] ?? config["DeepSeek:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? Environment.GetEnvironmentVariable("DeepseekKey");
        if (string.IsNullOrEmpty(apiKey))
            log.LogWarning("API key not set. Set Anthropic:ApiKey or DeepSeek:ApiKey in config, or ANTHROPIC_API_KEY or DeepseekKey environment variable.");

        var baseUrl = config["Anthropic:BaseUrl"] ?? config["DeepSeek:AnthropicBaseUrl"] ?? "https://api.deepseek.com/anthropic";
        var model = config["Anthropic:Model"] ?? config["DeepSeek:Model"] ?? "deepseek-reasoner";

        var clientOptions = new ClientOptions { ApiKey = apiKey ?? "", BaseUrl = baseUrl };
        var anthropicClient = new AnthropicClient(clientOptions);

        _agent = anthropicClient.AsAIAgent(
            model: model,
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

    public int GetContextWindowTokens() => _contextWindowTokens;

    public string? GetCachedSystemPromptForTokenCount() => contextFactory.GetCachedSystemPrompt();
}
