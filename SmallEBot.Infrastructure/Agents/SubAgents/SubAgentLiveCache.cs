using SmallEBot.Application.Contracts.Agents.SubAgents;
using SmallEBot.Core.Models;

namespace SmallEBot.Infrastructure.Agents.SubAgents;

/// <summary>
/// In-memory cache for running sub-agents. Thread-safe.
/// </summary>
public sealed class SubAgentLiveCache : ISubAgentLiveCache
{
    private readonly Dictionary<(Guid ConversationId, Guid SubAgentId), Entry> _entries = new();
    private readonly object _lock = new();

    public event Action? OnChanged;

    public void Register(Guid conversationId, Guid subAgentId, string subAgentName)
    {
        lock (_lock)
        {
            var key = (conversationId, subAgentId);
            if (!_entries.ContainsKey(key))
            {
                _entries[key] = new Entry(subAgentId, subAgentName, []);
            }
        }
        OnChanged?.Invoke();
    }

    public void AddUpdate(Guid conversationId, Guid subAgentId, string subAgentName, StreamUpdate update)
    {
        lock (_lock)
        {
            var key = (conversationId, subAgentId);
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry(subAgentId, subAgentName, []);
                _entries[key] = entry;
            }
            entry.Updates.Add(update);
        }
        OnChanged?.Invoke();
    }

    public void Complete(Guid conversationId, Guid subAgentId)
    {
        lock (_lock)
        {
            _entries.Remove((conversationId, subAgentId));
        }
        OnChanged?.Invoke();
    }

    public IReadOnlyList<SubAgentLiveEntry> GetRunning(Guid conversationId)
    {
        lock (_lock)
        {
            return _entries
                .Where(kv => kv.Key.ConversationId == conversationId)
                .Select(kv => new SubAgentLiveEntry(
                    kv.Value.SubAgentId,
                    kv.Value.SubAgentName,
                    kv.Value.Updates.ToList()))
                .ToList();
        }
    }

    private sealed class Entry(Guid subAgentId, string subAgentName, List<StreamUpdate> updates)
    {
        public Guid SubAgentId { get; } = subAgentId;
        public string SubAgentName { get; } = subAgentName;
        public List<StreamUpdate> Updates { get; } = updates;
    }
}
