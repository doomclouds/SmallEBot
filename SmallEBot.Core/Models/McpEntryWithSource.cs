namespace SmallEBot.Core.Models;

public record McpEntryWithSource(string Id, McpServerEntry Entry, bool IsSystem, bool IsEnabled);
