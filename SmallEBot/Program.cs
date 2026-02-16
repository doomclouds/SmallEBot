using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SmallEBot.Application.Conversation;
using SmallEBot.Application.Streaming;
using SmallEBot.Components;
using SmallEBot.Infrastructure.Data;
using SmallEBot.Infrastructure.Repositories;
using SmallEBot.Core.Repositories;
using SmallEBot.Services;

var builder = WebApplication.CreateBuilder(args);

// All data paths (DB, settings, MCP config) use app base directory
var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var dbPath = Path.Combine(baseDir, "smallebot.db");
var connectionString = $"Data Source={dbPath}";

builder.Services.AddDbContext<SmallEBotDbContext>(options =>
{
    options.UseSqlite(connectionString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.NonTransactionalMigrationOperationWarning));
});
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<BackfillTurnsService>();
builder.Services.AddScoped<UserPreferencesService>();
builder.Services.AddScoped<IMcpConfigService, McpConfigService>();
builder.Services.AddScoped<ISkillsConfigService, SkillsConfigService>();
builder.Services.AddScoped<IMcpToolsLoaderService, McpToolsLoaderService>();
builder.Services.AddScoped<IAgentContextFactory, AgentContextFactory>();
builder.Services.AddSingleton<IBuiltInToolFactory, BuiltInToolFactory>();
builder.Services.AddScoped<IMcpToolFactory, McpToolFactory>();
builder.Services.AddScoped<IAgentBuilder, AgentBuilder>();
builder.Services.AddScoped<IAgentConversationService, AgentConversationService>();
builder.Services.AddScoped<IAgentRunner, AgentRunnerAdapter>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<UserNameService>();
builder.Services.AddSingleton<MarkdownService>();
builder.Services.AddSingleton<ITokenizer>(sp =>
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
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply pending EF Core migrations and backfill TurnId for existing data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmallEBotDbContext>();
    db.Database.Migrate();
    var backfill = scope.ServiceProvider.GetRequiredService<BackfillTurnsService>();
    await backfill.RunAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
