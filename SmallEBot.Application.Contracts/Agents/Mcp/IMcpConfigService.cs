using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Mcp;

public interface IMcpConfigService
{
    Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default);
    Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDisabledSystemIdsAsync(CancellationToken ct = default);
    Task SetDisabledSystemIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
}
