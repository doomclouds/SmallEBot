using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Domain.Agents;
using SmallEBot.Domain.Common.Services;
using SmallEBot.Domain.Conversations;
using SmallEBot.Domain.UserPreferences;
using SmallEBot.Application.Contracts.Session;
using SmallEBot.Infrastructure.Conversations;
using SmallEBot.Infrastructure.Persistence.AgentSession;
using SmallEBot.Infrastructure.Session;
using SmallEBot.Infrastructure.Persistence.Repositories;
using SmallEBot.Infrastructure.UserPreferences;
using SmallEBot.Infrastructure.Services;
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

        services.AddScoped<IAgentSessionReader, AgentSessionReader>();

        services.AddSingleton<IConversationSessionCoordinator, ConversationSessionCoordinator>();

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
