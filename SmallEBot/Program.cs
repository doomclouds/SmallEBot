using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using SmallEBot.Components;
using SmallEBot.Extensions;
using SmallEBot.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmallEBotHostServices(builder.Configuration);
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmallEBotDbContext>();
    db.Database.Migrate();
    var backfill = scope.ServiceProvider.GetRequiredService<BackfillTurnsService>();
    await backfill.RunAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
