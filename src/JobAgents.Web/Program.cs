using System.Text;
using System.Text.Json;
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
// Stored OUTSIDE the repo so it survives `git clean -dfx`, rebuilds, and branch switches, and so every
// launch method shares one store. Previously rooted at ContentRootPath/results, which split data between
// `dotnet run` (project dir) and a built-dll launch (bin/.../results) and was wiped by clean. Override with
// the ResultsDirectory config key (env: ResultsDirectory) for tests/CI isolation.
var resultsDir = builder.Configuration["ResultsDirectory"] is { Length: > 0 } configured
    ? configured
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JobAgents", "results");
builder.Services.AddSingleton(_ => new RunStore(resultsDir));
builder.Services.AddSingleton(_ => new JobAgents.Infrastructure.Feedback.FeedbackStore(resultsDir));
builder.Services.AddSingleton(_ => new ProfileStore(resultsDir));
builder.Services.AddSingleton(_ => new ModelConfigStore(resultsDir));
builder.Services.AddSingleton(_ => new IdeaStore(resultsDir));

// Retrieve-before-fetch posting corpus (cuts Tavily calls + grows the result pool across runs).
builder.Services.AddSingleton<JobAgents.Infrastructure.Sourcing.IPostingStore>(
    _ => new JobAgents.Infrastructure.Sourcing.FilePostingStore(resultsDir));

// Disk-backed Tavily response cache (48h TTL): identical queries across runs/restarts replay the same
// rows with no HTTP call — fewer Tavily requests + credits, identical results. Overrides the memory-only
// default registered in AddInfrastructure.
builder.Services.AddSingleton(
    _ => new JobAgents.Infrastructure.Plugins.TavilySearchCache(Path.Combine(resultsDir, "tavily-cache")));

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

// Download all saved runs as a JSON file.
app.MapGet("/export/runs", async (RunStore store, CancellationToken ct) =>
{
    var runs = await store.LoadAllAsync(ct);
    var json = JsonSerializer.Serialize(runs, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    });
    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "jobagents-runs.json");
});

app.Run();
