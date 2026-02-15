using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SmallEBot.Models;

namespace SmallEBot.Services;

/// <summary>Result of loading tools (and optionally prompts) for one MCP server.</summary>
public sealed record McpToolsLoadResult(
    IReadOnlyList<McpToolInfo> Tools,
    IReadOnlyList<McpPromptInfo>? Prompts,
    string? Error);

public sealed record McpToolInfo(string Name, string? Description);

public sealed record McpPromptInfo(string Name, string? Description);

/// <summary>Loads tools and prompts for a single MCP server entry (for UI display).</summary>
public interface IMcpToolsLoaderService
{
    Task<McpToolsLoadResult> LoadAsync(string id, McpServerEntry entry, CancellationToken ct = default);
}

public class McpToolsLoaderService : IMcpToolsLoaderService
{
    public async Task<McpToolsLoadResult> LoadAsync(string id, McpServerEntry entry, CancellationToken ct = default)
    {
        IAsyncDisposable? client = null;
        try
        {
            var isStdio = "stdio".Equals(entry.Type, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(entry.Type) && !string.IsNullOrEmpty(entry.Command));

            if (isStdio)
            {
                if (string.IsNullOrEmpty(entry.Command))
                    return new McpToolsLoadResult([], null, "Missing command");
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = id,
                    Command = entry.Command,
                    Arguments = entry.Args ?? [],
                    EnvironmentVariables = entry.Env ?? new Dictionary<string, string?>()
                });
                client = await McpClient.CreateAsync(transport, null, null, ct);
            }
            else if ("http".Equals(entry.Type, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(entry.Url))
                    return new McpToolsLoadResult([], null, "Missing URL");
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(entry.Url),
                    TransportMode = HttpTransportMode.AutoDetect,
                    ConnectionTimeout = TimeSpan.FromSeconds(30)
                });
                client = await McpClient.CreateAsync(transport, null, null, ct);
            }
            else
                return new McpToolsLoadResult([], null, "Unknown type");

            var mcpClient = (McpClient)client;
            var tools = await mcpClient.ListToolsAsync(cancellationToken: ct);
            var toolList = tools.Select(t => new McpToolInfo(t.Name, t.Description)).ToList();

            List<McpPromptInfo>? prompts = null;
            try
            {
                var promptsMethod = mcpClient.GetType().GetMethod("ListPromptsAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (promptsMethod != null)
                {
                    var task = (Task)promptsMethod.Invoke(mcpClient, [ct])!;
                    await task;
                    var resultProp = task.GetType().GetProperty("Result");
                    var result = resultProp?.GetValue(task) as IReadOnlyList<dynamic>;
                    if (result != null)
                        prompts = result.Select(p => new McpPromptInfo((string)p.Name, (string?)p.Description)).ToList();
                }
            }
            catch
            {
                // ListPromptsAsync not available or failed; prompts section skipped until client supports it
            }

            return new McpToolsLoadResult(toolList, prompts, null);
        }
        catch (Exception ex)
        {
            return new McpToolsLoadResult([], null, ex.Message);
        }
        finally
        {
            if (client != null)
                await client.DisposeAsync();
        }
    }
}
