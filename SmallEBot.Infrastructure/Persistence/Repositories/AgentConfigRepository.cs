using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SmallEBot.Domain.Agents;
using SmallEBot.Domain.Agents.ValueObjects;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// File-based implementation of IAgentConfigRepository with in-memory caching.
/// Stores agent configurations in .agents/agents.json with structure:
/// { "defaultAgentId": "...", "agents": [...] }
/// Thread-safe with ReaderWriterLockSlim for concurrent access and lazy loading.
/// </summary>
public sealed class AgentConfigRepository : IAgentConfigRepository, IDisposable
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, AgentConfig> _cache = new();
    private string? _defaultAgentId;
    private bool _isLoaded;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of AgentConfigRepository.
    /// </summary>
    /// <param name="basePath">The base path for storing agent data (application root directory).</param>
    public AgentConfigRepository(string basePath)
    {
        ArgumentNullException.ThrowIfNull(basePath);

        _filePath = Path.Combine(basePath, ".agents", "agents.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
    }

    /// <inheritdoc />
    public async Task<AgentConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterReadLock();
        try
        {
            if (_defaultAgentId is null || !_cache.TryGetValue(_defaultAgentId, out var agent))
            {
                return null;
            }

            return agent;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task<AgentConfig?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(id);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterReadLock();
        try
        {
            return _cache.TryGetValue(id, out var agent) ? agent : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConfig>> GetAllAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterReadLock();
        try
        {
            return _cache.Values.ToList().AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AgentConfig agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(agent);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterWriteLock();
        try
        {
            // Update or add the agent in cache
            _cache[agent.Id] = agent;

            // If this is the first agent or marked as default, update default ID
            if (agent.IsDefault)
            {
                // Clear IsDefault on other agents
                foreach (var existing in _cache.Values.Where(a => a.Id != agent.Id))
                {
                    existing.IsDefault = false;
                }

                _defaultAgentId = agent.Id;
            }
            else if (_cache.Count == 1)
            {
                // First agent becomes default automatically
                agent.IsDefault = true;
                _defaultAgentId = agent.Id;
            }

            // Persist to file
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(id);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterWriteLock();
        try
        {
            if (!_cache.Remove(id))
            {
                return; // Agent didn't exist, nothing to do
            }

            // If we deleted the default agent, clear the default ID
            if (_defaultAgentId == id)
            {
                _defaultAgentId = null;

                // If there are other agents, pick the first one as new default
                var firstAgent = _cache.Values.FirstOrDefault();
                if (firstAgent is not null)
                {
                    firstAgent.IsDefault = true;
                    _defaultAgentId = firstAgent.Id;
                }
            }

            // Persist to file
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public async Task SetDefaultAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(id);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        _lock.EnterWriteLock();
        try
        {
            if (!_cache.TryGetValue(id, out var agent))
            {
                throw new InvalidOperationException($"Agent with ID '{id}' not found.");
            }

            // Clear IsDefault on all agents
            foreach (var existing in _cache.Values)
            {
                existing.IsDefault = false;
            }

            // Set new default
            agent.IsDefault = true;
            _defaultAgentId = id;

            // Persist to file
            await PersistAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Ensures the agent configurations are loaded from file into cache.
    /// Uses double-checked locking with EnterUpgradeableReadLock for lazy loading.
    /// </summary>
    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        // First check without lock (fast path)
        if (_isLoaded)
        {
            return;
        }

        // Use upgradeable read lock - allows reading and potential upgrade to write lock
        _lock.EnterUpgradeableReadLock();
        try
        {
            // Double-check after acquiring lock
            if (_isLoaded)
            {
                return;
            }

            // Upgrade to write lock for loading
            _lock.EnterWriteLock();
            try
            {
                // Triple-check after upgrading (defensive)
                if (_isLoaded)
                {
                    return;
                }

                await LoadFromFileAsync(ct).ConfigureAwait(false);
                _isLoaded = true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Loads agent configurations from the JSON file into cache.
    /// Must be called within a write lock.
    /// </summary>
    private async Task LoadFromFileAsync(CancellationToken ct)
    {
        _cache.Clear();
        _defaultAgentId = null;

        if (!File.Exists(_filePath))
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // File might be locked or inaccessible
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        AgentConfigFile? data;
        try
        {
            data = JsonSerializer.Deserialize<AgentConfigFile>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            // Invalid JSON format
            return;
        }

        if (data?.Agents is null || data.Agents.Length == 0)
        {
            return;
        }

        foreach (var dto in data.Agents)
        {
            var agent = MapToEntity(dto);
            _cache[agent.Id] = agent;
        }

        // Set default agent ID
        _defaultAgentId = data.DefaultAgentId;

        // Ensure IsDefault flag is consistent
        if (_defaultAgentId is not null && _cache.TryGetValue(_defaultAgentId, out var defaultAgent))
        {
            defaultAgent.IsDefault = true;
        }
    }

    /// <summary>
    /// Persists the current cache state to the JSON file.
    /// Must be called within a write lock.
    /// </summary>
    private async Task PersistAsync(CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var agents = _cache.Values.Select(MapToDto).ToArray();
        var data = new AgentConfigFile
        {
            DefaultAgentId = _defaultAgentId,
            Agents = agents
        };

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps a DTO to a domain entity.
    /// </summary>
    private static AgentConfig MapToEntity(AgentConfigDto dto)
    {
        var agent = new AgentConfig(
            dto.Id ?? throw new InvalidOperationException("Agent ID is required"),
            dto.Name ?? "Unnamed Agent",
            dto.Description ?? string.Empty,
            dto.Instructions ?? string.Empty,
            dto.ModelId ?? string.Empty)
        {
            IsDefault = dto.IsDefault,
            Tools = dto.Tools is not null
                ? new ToolSet(
                    dto.Tools.BuiltInTools ?? [],
                    dto.Tools.McpTools ?? [],
                    dto.Tools.InheritParent)
                : ToolSet.Full,
            McpServerIds = dto.McpServerIds ?? [],
            SkillIds = dto.SkillIds ?? ["*"],
            Terminal = dto.Terminal is not null
                ? new TerminalConfig(
                    dto.Terminal.CommandBlacklist ?? [],
                    dto.Terminal.CommandWhitelist ?? [],
                    TimeSpan.FromMilliseconds(dto.Terminal.CommandTimeoutMs > 0
                        ? dto.Terminal.CommandTimeoutMs
                        : 60_000),
                    dto.Terminal.RequireConfirmation,
                    TimeSpan.FromMilliseconds(dto.Terminal.ConfirmationTimeoutMs > 0
                        ? dto.Terminal.ConfirmationTimeoutMs
                        : 60_000))
                : TerminalConfig.Default
        };

        // Map sub-agents
        if (dto.SubAgents is not null)
        {
            foreach (var subDto in dto.SubAgents)
            {
                var subAgent = new SubAgentConfig(
                    subDto.Id ?? throw new InvalidOperationException("Sub-agent ID is required"),
                    subDto.Name ?? "Unnamed Sub-agent",
                    subDto.Description ?? string.Empty,
                    subDto.Instructions ?? string.Empty,
                    subDto.HandoffMode)
                {
                    IsEnabled = subDto.IsEnabled,
                    ModelOverride = subDto.ModelOverride is not null
                        ? new ModelConfig(
                            subDto.ModelOverride.Id ?? string.Empty,
                            subDto.ModelOverride.Name ?? string.Empty,
                            subDto.ModelOverride.Provider ?? string.Empty,
                            subDto.ModelOverride.BaseUrl ?? string.Empty,
                            subDto.ModelOverride.ApiKeySource ?? string.Empty,
                            subDto.ModelOverride.ModelId ?? string.Empty,
                            subDto.ModelOverride.ContextWindow > 0
                                ? subDto.ModelOverride.ContextWindow
                                : 128_000,
                            subDto.ModelOverride.SupportsThinking)
                        : null,
                    Tools = subDto.Tools is not null
                        ? new ToolSet(
                            subDto.Tools.BuiltInTools ?? [],
                            subDto.Tools.McpTools ?? [],
                            subDto.Tools.InheritParent)
                        : null
                };

                agent.AddSubAgent(subAgent);
            }
        }

        return agent;
    }

    /// <summary>
    /// Maps a domain entity to a DTO.
    /// </summary>
    private static AgentConfigDto MapToDto(AgentConfig agent)
    {
        var dto = new AgentConfigDto
        {
            Id = agent.Id,
            Name = agent.Name,
            Description = agent.Description,
            Instructions = agent.Instructions,
            ModelId = agent.ModelId,
            IsDefault = agent.IsDefault,
            Tools = new ToolSetDto
            {
                BuiltInTools = agent.Tools.BuiltInTools,
                McpTools = agent.Tools.McpTools,
                InheritParent = agent.Tools.InheritParent
            },
            McpServerIds = agent.McpServerIds,
            SkillIds = agent.SkillIds,
            Terminal = new TerminalConfigDto
            {
                CommandBlacklist = agent.Terminal.CommandBlacklist,
                CommandWhitelist = agent.Terminal.CommandWhitelist,
                CommandTimeoutMs = (int)agent.Terminal.CommandTimeout.TotalMilliseconds,
                RequireConfirmation = agent.Terminal.RequireConfirmation,
                ConfirmationTimeoutMs = (int)agent.Terminal.ConfirmationTimeout.TotalMilliseconds
            },
            SubAgents = agent.SubAgents.Select(sa => new SubAgentConfigDto
            {
                Id = sa.Id,
                Name = sa.Name,
                Description = sa.Description,
                Instructions = sa.Instructions,
                HandoffMode = sa.HandoffMode,
                IsEnabled = sa.IsEnabled,
                ModelOverride = sa.ModelOverride is not null
                    ? new ModelConfigDto
                    {
                        Id = sa.ModelOverride.Id,
                        Name = sa.ModelOverride.Name,
                        Provider = sa.ModelOverride.Provider,
                        BaseUrl = sa.ModelOverride.BaseUrl,
                        ApiKeySource = sa.ModelOverride.ApiKeySource,
                        ModelId = sa.ModelOverride.ModelId,
                        ContextWindow = sa.ModelOverride.ContextWindow,
                        SupportsThinking = sa.ModelOverride.SupportsThinking
                    }
                    : null,
                Tools = sa.Tools is not null
                    ? new ToolSetDto
                    {
                        BuiltInTools = sa.Tools.BuiltInTools,
                        McpTools = sa.Tools.McpTools,
                        InheritParent = sa.Tools.InheritParent
                    }
                    : null
            }).ToArray()
        };

        return dto;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentConfigRepository));
        }
    }

    /// <summary>
    /// Releases all resources used by the AgentConfigRepository.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Dispose();
        _disposed = true;
    }

    #region DTO Classes

    /// <summary>
    /// File structure for agents.json
    /// </summary>
    private sealed class AgentConfigFile
    {
        public string? DefaultAgentId { get; set; }
        public AgentConfigDto[]? Agents { get; set; }
    }

    private sealed class AgentConfigDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? ModelId { get; set; }
        public bool IsDefault { get; set; }
        public ToolSetDto? Tools { get; set; }
        public string[]? McpServerIds { get; set; }
        public string[]? SkillIds { get; set; }
        public TerminalConfigDto? Terminal { get; set; }
        public SubAgentConfigDto[]? SubAgents { get; set; }
    }

    private sealed class ToolSetDto
    {
        public string[]? BuiltInTools { get; set; }
        public string[]? McpTools { get; set; }
        public bool InheritParent { get; set; }
    }

    private sealed class TerminalConfigDto
    {
        public string[]? CommandBlacklist { get; set; }
        public string[]? CommandWhitelist { get; set; }
        public int CommandTimeoutMs { get; set; }
        public bool RequireConfirmation { get; set; }
        public int ConfirmationTimeoutMs { get; set; }
    }

    private sealed class SubAgentConfigDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public HandoffMode HandoffMode { get; set; }
        public bool IsEnabled { get; set; } = true;
        public ModelConfigDto? ModelOverride { get; set; }
        public ToolSetDto? Tools { get; set; }
    }

    private sealed class ModelConfigDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Provider { get; set; }
        public string? BaseUrl { get; set; }
        public string? ApiKeySource { get; set; }
        public string? ModelId { get; set; }
        public int ContextWindow { get; set; }
        public bool SupportsThinking { get; set; }
    }

    #endregion
}
