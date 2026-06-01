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

// Run persistence + Past Runs reader, and the reusable saved-CV store.
var resultsDir = Path.Combine(builder.Environment.ContentRootPath, "results");
builder.Services.AddSingleton(_ => new RunStore(resultsDir));
builder.Services.AddSingleton(_ => new ProfileStore(resultsDir));

// Resume file → text extraction (PDF / DOCX / TXT).
builder.Services.AddSingleton<ResumeTextExtractor>();

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
