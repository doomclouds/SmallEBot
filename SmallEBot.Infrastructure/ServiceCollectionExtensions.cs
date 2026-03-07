using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Domain.Agents;
using SmallEBot.Domain.Conversations;
using SmallEBot.Domain.UserPreferences;
using SmallEBot.Domain.Workspaces;
using SmallEBot.Infrastructure.Persistence.AgentSession;
using SmallEBot.Infrastructure.Persistence.Repositories;

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
        services.AddSingleton<IAgentConfigRepository>(sp =>
            new AgentConfigRepository(basePath));

        services.AddSingleton<IConversationMetadataRepository>(sp =>
            new ConversationMetadataRepository(basePath));

        services.AddSingleton<IUserPreferenceRepository>(sp =>
            new UserPreferenceRepository(basePath));

        services.AddSingleton<IWorkspaceRepository>(sp =>
            new WorkspaceRepository(basePath));

        // AgentSession storage - requires AIAgent for serialization
        // Note: AgentSessionSerializer needs AIAgent, so it's a transient dependency
        services.AddTransient<AgentSessionSerializer>(sp =>
        {
            var agent = sp.GetRequiredService<AIAgent>();
            return new AgentSessionSerializer(agent);
        });

        services.AddSingleton<IAgentSessionStore>(sp =>
        {
            var serializer = sp.GetRequiredService<AgentSessionSerializer>();
            return new AgentSessionStore(basePath, serializer);
        });

        return services;
    }
}
