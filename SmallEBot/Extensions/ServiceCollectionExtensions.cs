using SmallEBot.Application.Conversation;
using SmallEBot.Services.Agent;
using SmallEBot.Services.Conversation;
using SmallEBot.Services.Mcp;
using SmallEBot.Services.Presentation;
using SmallEBot.Services.Skills;
using Microsoft.AspNetCore.Components.Server.Circuits;
using SmallEBot.Services.Circuit;
using SmallEBot.Services.Terminal;
using SmallEBot.Services.User;
using SmallEBot.Services.Context;
using SmallEBot.Services.Agent.Tools;
using SmallEBot.Components.Chat.Services;
using SmallEBot.Components.Chat.State;
using SmallEBot.Services.Session;
using SmallEBot.Infrastructure;
using Microsoft.Agents.AI;
using SmallEBot.Application.Contracts.Agents;
using SmallEBot.Application.Contracts.Context;
using SmallEBot.Application.Contracts.Conversation;
using SmallEBot.Application.Contracts.Session;
using SmallEBot.Application.Contracts.Streaming;
using SmallEBot.Application.Contracts.User;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Application.Workspaces;
using SmallEBot.Infrastructure.Workspaces;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers all SmallEBot Host services: Session file storage, Agent pipeline, MCP, Skills, and UI services.</summary>
    public static IServiceCollection AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(baseDir, "smallebot.db");

        // File-based session services
        services.AddSingleton<ISessionFileService, SessionFileService>();
        services.AddScoped<IAgentSessionReader, AgentSessionReader>();
        services.AddScoped<SessionManager>();
        services.AddScoped<ISessionManager>(sp => sp.GetRequiredService<SessionManager>());
        services.AddScoped<ISessionAgentManager>(sp => sp.GetRequiredService<SessionManager>());

        services.AddSingleton<IConversationTaskContext, ConversationTaskContext>();
        services.AddSingleton<ICurrentConversationService, CurrentConversationService>();
        services.AddSingleton<ITaskListService, TaskListService>();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        // IVirtualFileSystem and IWorkspaceWatcher are registered in Infrastructure layer with factory delegates
        // These registrations are removed here to avoid duplicate registrations
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();
        services.AddScoped<IMcpToolsLoaderService, McpToolsLoaderService>();
        services.AddScoped<IAgentContextFactory, AgentContextFactory>();
        services.AddSingleton<IToolProvider, TimeToolProvider>();
        services.AddSingleton<IToolProvider, FileToolProvider>();
        services.AddSingleton<IToolProvider, SearchToolProvider>();
        services.AddSingleton<IToolProvider, ShellToolProvider>();
        services.AddSingleton<IToolProvider, TaskToolProvider>();
        services.AddScoped<IToolProvider, SkillGenerationToolProvider>();
        services.AddScoped<IToolProviderAggregator, ToolProviderAggregator>();
        services.AddSingleton<ITaskListCache, TaskListCache>();
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

        services.AddScoped<IAgentConversationService, AgentConversationService>();
        services.AddScoped<IAgentRunner, AgentRunnerAdapter>();
        services.AddScoped<ITurnContextFragmentBuilder, TurnContextFragmentBuilder>();
        services.AddScoped<ConversationService>();
        services.AddScoped<AgentCacheService>();
        services.AddScoped<UserNameService>();
        services.AddScoped<IUserNameProvider>(sp => sp.GetRequiredService<UserNameService>());
        services.AddScoped<ICurrentCircuitAccessor, CurrentCircuitAccessor>();
        services.AddScoped<CircuitHandler, CircuitContextHandler>();
        services.AddSingleton<MarkdownService>();
        services.AddScoped<KeyboardShortcutService>();
        services.AddScoped<ChatState>();
        services.AddScoped<IContextUsageEstimator>(sp => sp.GetRequiredService<AgentCacheService>());
        services.AddScoped<ChatPresentationService>();
        services.AddSingleton<IContextWindowManager, ContextWindowManager>();
        services.AddSingleton<ITerminalConfigService, TerminalConfigService>();
        services.AddScoped<ISkillsConfigService, SkillsConfigService>();
        services.AddSingleton<IMcpConfigService, McpConfigService>();
        services.AddSingleton<UserPreferencesService>();
        return services;
    }
}
