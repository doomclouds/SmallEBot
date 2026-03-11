using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Domain.Agents.Config;
using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Application.Contracts.Agents.SubAgents;
using SmallEBot.Domain.Conversations.Metadata;
using SmallEBot.Domain.UserPreferences;
using SmallEBot.Infrastructure.Agents.Config;
using SmallEBot.Infrastructure.Agents.Streaming;
using SmallEBot.Infrastructure.Agents.Tokenizers;
using SmallEBot.Infrastructure.Conversations;
using SmallEBot.Infrastructure.Conversations.Metadata;
using SmallEBot.Application.Agents.SubAgents;
using SmallEBot.Infrastructure.Agents.SubAgents;
using SmallEBot.Infrastructure.Conversations.Session;
using SmallEBot.Infrastructure.Conversations.TaskList;
using SmallEBot.Infrastructure.UserPreferences;
using SmallEBot.Infrastructure.Workspaces;

namespace SmallEBot.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure layer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Infrastructure layer services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="basePath">The base path for file-based storage (application root directory).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string basePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(basePath);

        // Repositories (file-based, no external dependencies)
        services.AddSingleton<IAgentConfigRepository>(_ =>
            new AgentConfigRepository(basePath));

        services.AddSingleton<IConversationMetadataRepository>(_ =>
            new ConversationMetadataRepository(basePath));

        services.AddSingleton<IUserPreferenceRepository>(_ =>
            new UserPreferenceRepository(basePath));

        // AgentSession storage - requires AIAgent for serialization
        // Note: AgentSessionSerializer needs AIAgent, so it's resolved lazily via IServiceProvider
        // to avoid Singleton capturing a Transient/Scoped dependency.
        services.AddTransient<AgentSessionSerializer>(sp =>
        {
            var agent = sp.GetRequiredService<AIAgent>();
            return new AgentSessionSerializer(agent);
        });

        services.AddSingleton<IAgentSessionStore>(_ => new AgentSessionStore(basePath));
        services.AddSingleton<ISubAgentSessionStore>(_ => new SubAgentSessionStore(basePath));
        services.AddScoped<ISubAgentRunner, SubAgentRunner>();
        services.AddSingleton<ISubAgentRunningRegistry, SubAgentRunningRegistry>();
        services.AddSingleton<ISubAgentLiveCache, SubAgentLiveCache>();
        services.AddScoped<SubAgentOrchestrator>();

        services.AddScoped<IAgentSessionReader, AgentSessionReader>();
        services.AddScoped<IConversationMessageStore, ConversationMessageStore>();

        services.AddSingleton(_ => new TaskListCache(basePath));
        services.AddSingleton<ITaskListService, TaskListService>();
        services.AddSingleton<IAmbientConversationId, AmbientConversationId>();
        services.AddSingleton<IAmbientStreamSink, AmbientStreamSink>();
        services.AddSingleton<ICurrentConversationService, CurrentConversationService>();

        // Tokenizer services
        services.AddSingleton<ITokenizer>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var path = config["Anthropic:TokenizerPath"];
            try
            {
                return new DeepSeekTokenizer(path);
            }
            catch (FileNotFoundException)
            {
                return new CharEstimateTokenizer();
            }
        });

        // Workspace services
        var workspaceRoot = Path.Combine(basePath, ".agents", "vfs");

        services.AddSingleton<IVirtualFileSystem>(sp =>
            new VirtualFileSystem(workspaceRoot, sp.GetRequiredService<ILogger<VirtualFileSystem>>()));

        services.AddSingleton<IWorkspaceWatcher>(_ =>
            new WorkspaceWatcher(workspaceRoot));

        return services;
    }
}
