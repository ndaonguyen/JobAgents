using JobAgents.Application;
using JobAgents.Infrastructure.DependencyInjection;
using JobAgents.Web.Components;
using JobAgents.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Application + Infrastructure (the Coordinator, agents, event bus, Tavily search, pricing).
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// Run persistence + Past Runs reader.
builder.Services.AddSingleton(_ => new RunStore(
    Path.Combine(builder.Environment.ContentRootPath, "results")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
