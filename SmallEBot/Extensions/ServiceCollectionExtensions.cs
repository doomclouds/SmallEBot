using SmallEBot.Application.Agents.Compression;
using SmallEBot.Application.Agents.Execution;
using SmallEBot.Application.Conversations;
using SmallEBot.Services.Presentation;
using Microsoft.AspNetCore.Components.Server.Circuits;
using SmallEBot.Services.Circuit;
using SmallEBot.Application.Contracts.Agents.Config;
using SmallEBot.Application.Contracts.Agents.Tools;
using SmallEBot.Application.Contracts.Agents.Mcp;
using SmallEBot.Application.UserPreferences;
using SmallEBot.Infrastructure.Agents.Config;
using SmallEBot.Infrastructure.Agents.Mcp;
using SmallEBot.Infrastructure.Agents.Skills;
using SmallEBot.Infrastructure.Agents.Tools;
using SmallEBot.Components.Chat.Services;
using SmallEBot.Infrastructure;
using Microsoft.Agents.AI;
using SmallEBot.Application.Agents.Context;
using SmallEBot.Application.Contracts.Agents.Context;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.Skills;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.UserPreferences;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Domain.Workspaces;
using SmallEBot.Application.Workspaces;

namespace SmallEBot.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers all SmallEBot Host services: Session file storage, Agent pipeline, MCP, Skills, and UI services.</summary>
    public static IServiceCollection AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // IConversationSessionCoordinator, IAgentSessionReader, IAmbientConversationId, ICurrentConversationService,
        // ITaskListService, IAmbientConversationId, ICurrentConversationService are registered in Infrastructure.AddInfrastructure
        // IConversationMetadataRepository is registered in Infrastructure.AddInfrastructure

        services.AddSingleton<ICommandRunner, CommandRunner>();
        // IVirtualFileSystem and IWorkspaceWatcher are registered in Infrastructure layer with factory delegates
        // These registrations are removed here to avoid duplicate registrations
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IWorkspaceUploadService, WorkspaceUploadService>();
        services.AddScoped<IMcpToolsLoaderService, McpToolsLoaderService>();
        services.AddScoped<IAgentSystemPromptBuilder, AgentSystemPromptBuilder>();
        services.AddSingleton<IToolProvider, TimeToolProvider>();
        services.AddSingleton<IWorkspaceReadOnlyPolicy, WorkspaceReadOnlyPolicy>();
        services.AddSingleton<IToolProvider, FileToolProvider>();
        services.AddSingleton<IToolProvider, SearchToolProvider>();
        services.AddSingleton<IToolProvider, ShellToolProvider>();
        services.AddSingleton<IToolProvider, TaskToolProvider>();
        services.AddScoped<IToolProvider, SkillGenerationToolProvider>();
        services.AddScoped<IToolProviderAggregator, ToolProviderAggregator>();
        services.AddSingleton<IModelConfigService, ModelConfigService>();
        services.AddSingleton<AgentConfigService>();
        services.AddSingleton<IAgentConfigService>(sp => sp.GetRequiredService<AgentConfigService>());
        services.AddSingleton<IToolResultMaxProvider>(sp => sp.GetRequiredService<AgentConfigService>());
        services.AddSingleton<ICompressionThresholdProvider>(sp => sp.GetRequiredService<AgentConfigService>());
        services.AddScoped<ICompressionService, CompressionService>();
        services.AddSingleton<IMcpConnectionManager, McpConnectionManager>();
        services.AddScoped<IAgentBuilder, AgentBuilder>();

        // Register AIAgent factory for Infrastructure layer (AgentSessionSerializer needs it)
        // Note: AIAgent is created on-demand via IAgentBuilder.GetOrCreateAgentAsync()
        services.AddScoped<AIAgent>(sp =>
        {
            var agentBuilder = sp.GetRequiredService<IAgentBuilder>();
            // Use GetAwaiter().GetResult() since DI factories must be synchronous
            // This is acceptable because AgentBuilder caches the agent after first creation
            return agentBuilder.GetOrCreateAgentAsync(useThinking: false).GetAwaiter().GetResult();
        });

        // Infrastructure layer (repositories, AgentSessionStore)
        services.AddInfrastructure(baseDir);

        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IConversationAgentDispatcher, ConversationAgentDispatcher>();
        services.AddScoped<IAgentRunner, AgentRunner>();
        services.AddScoped<ITurnContextFragmentBuilder, TurnContextFragmentBuilder>();
        services.AddScoped<IAgentInvalidationService, AgentInvalidationService>();
        services.AddScoped<IContextUsageEstimator, ContextUsageEstimator>();
        services.AddSingleton<IUserPreferencesService, UserPreferencesService>();
        services.AddSingleton<IUserNameDisplayService, UserNameDisplayService>();
        services.AddScoped<ICurrentCircuitAccessor, CurrentCircuitAccessor>();
        services.AddScoped<CircuitHandler, CircuitContextHandler>();
        services.AddSingleton<MarkdownService>();
        services.AddScoped<KeyboardShortcutService>();
        services.AddScoped<ChatPresentationService>();
        services.AddSingleton<ITerminalConfigService, TerminalConfigService>();
        services.AddScoped<ISkillsConfigService, SkillsConfigService>();
        services.AddSingleton<IMcpConfigService, McpConfigService>();
        return services;
    }
}
