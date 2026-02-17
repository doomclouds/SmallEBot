using Microsoft.EntityFrameworkCore;
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Streaming;
using SmallEBot.Infrastructure.Data;
using SmallEBot.Infrastructure.Repositories;
using SmallEBot.Core.Repositories;
using SmallEBot.Services.Agent;
using SmallEBot.Services.Conversation;
using SmallEBot.Services.Mcp;
using SmallEBot.Services.Presentation;
using SmallEBot.Services.Skills;
using SmallEBot.Services.Sandbox;
using SmallEBot.Services.Terminal;
using SmallEBot.Services.User;

namespace SmallEBot.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers all SmallEBot Host services: DbContext, repositories, Application pipeline, MCP, Skills, Agent, and UI services.</summary>
    public static IServiceCollection AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(baseDir, "smallebot.db");
        var connectionString = $"Data Source={dbPath}";

        services.AddDbContext<SmallEBotDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.NonTransactionalMigrationOperationWarning));
        });
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<BackfillTurnsService>();
        services.AddScoped<UserPreferencesService>();
        services.AddScoped<IMcpConfigService, McpConfigService>();
        services.AddScoped<ISkillsConfigService, SkillsConfigService>();
        services.AddSingleton<ITerminalConfigService, TerminalConfigService>();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IPythonSandbox, ProcessPythonSandbox>();
        services.AddScoped<IMcpToolsLoaderService, McpToolsLoaderService>();
        services.AddScoped<IAgentContextFactory, AgentContextFactory>();
        services.AddSingleton<IBuiltInToolFactory, BuiltInToolFactory>();
        services.AddScoped<IMcpToolFactory, McpToolFactory>();
        services.AddScoped<IAgentBuilder, AgentBuilder>();
        services.AddScoped<IAgentConversationService, AgentConversationService>();
        services.AddScoped<IAgentRunner, AgentRunnerAdapter>();
        services.AddScoped<ConversationService>();
        services.AddScoped<AgentCacheService>();
        services.AddScoped<UserNameService>();
        services.AddSingleton<MarkdownService>();
        services.AddSingleton<ITokenizer>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var path = config["DeepSeek:TokenizerPath"];
            try
            {
                return new DeepSeekTokenizer(path);
            }
            catch (FileNotFoundException)
            {
                return new CharEstimateTokenizer();
            }
        });
        return services;
    }
}
