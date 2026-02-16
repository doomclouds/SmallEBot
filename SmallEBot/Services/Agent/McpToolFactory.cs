using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SmallEBot.Services.Mcp;

namespace SmallEBot.Services.Agent;

/// <summary>Loads MCP tools and clients for the agent. Caller owns disposal of returned clients (e.g. on Invalidate).</summary>
public interface IMcpToolFactory
{
    /// <summary>Loads all enabled MCP servers and returns their tools plus clients to hold. On per-entry failure, logs warning and skips that entry.</summary>
    Task<(AITool[] Tools, IReadOnlyList<IAsyncDisposable> Clients)> LoadAsync(CancellationToken ct = default);
}

public sealed class McpToolFactory(
    IMcpConfigService mcpConfig,
    ILogger<McpToolFactory> log) : IMcpToolFactory
{
    public async Task<(AITool[] Tools, IReadOnlyList<IAsyncDisposable> Clients)> LoadAsync(CancellationToken ct = default)
    {
        var tools = new List<AITool>();
        var clients = new List<IAsyncDisposable>();

        var allEntries = await mcpConfig.GetAllAsync(ct);

        foreach (var (id, entry, _, isEnabled) in allEntries)
        {
            if (!isEnabled) continue;

            var isStdio = "stdio".Equals(entry.Type, StringComparison.OrdinalIgnoreCase)
                          || (string.IsNullOrEmpty(entry.Type) && !string.IsNullOrEmpty(entry.Command));

            if (isStdio)
            {
                if (string.IsNullOrEmpty(entry.Command))
                {
                    log.LogWarning("MCP stdio server '{Name}' has no command, skipped.", id);
                    continue;
                }
                try
                {
                    var transport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = id,
                        Command = entry.Command,
                        Arguments = entry.Args ?? [],
                        EnvironmentVariables = entry.Env ?? new Dictionary<string, string?>()
                    });
                    var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                    clients.Add(mcpClient);
                    var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: ct);
                    tools.AddRange(mcpTools);
                    log.LogInformation("MCP stdio server '{Name}' ({Command}) loaded with {Count} tools.", id, entry.Command, mcpTools.Count);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to load MCP stdio server '{Name}' (command: {Command}), skipping.", id, entry.Command);
                }
                continue;
            }

            if ("http".Equals(entry.Type, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(entry.Url))
                {
                    log.LogWarning("MCP http server '{Name}' has no url, skipped.", id);
                    continue;
                }
                try
                {
                    var options = new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(entry.Url),
                        TransportMode = HttpTransportMode.AutoDetect,
                        ConnectionTimeout = TimeSpan.FromSeconds(30)
                    };
                    HttpClientTransport transport;
                    if (entry.Headers is { Count: > 0 })
                    {
                        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                        foreach (var h in entry.Headers)
                        {
                            if (string.IsNullOrEmpty(h.Key)) continue;
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value ?? "");
                        }
                        transport = new HttpClientTransport(options, httpClient, ownsHttpClient: true);
                    }
                    else
                    {
                        transport = new HttpClientTransport(options);
                    }
                    var mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
                    clients.Add(mcpClient);
                    var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: ct);
                    tools.AddRange(mcpTools);
                    log.LogInformation("MCP http server '{Name}' at {Url} loaded with {Count} tools.", id, entry.Url, mcpTools.Count);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to load MCP http server '{Name}' at {Url}, skipping.", id, entry.Url);
                }
            }
        }

        return (tools.ToArray(), clients);
    }
}
