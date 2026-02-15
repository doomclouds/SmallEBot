using System.IO;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SmallEBot.Components;
using SmallEBot.Data;
using SmallEBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=smallebot.db");
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.NonTransactionalMigrationOperationWarning));
});
builder.Services.AddScoped<UserPreferencesService>();
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
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    var convSvc = scope.ServiceProvider.GetRequiredService<ConversationService>();
    convSvc.BackfillTurnsAsync().GetAwaiter().GetResult();
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
