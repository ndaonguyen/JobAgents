using System.Text;
using System.Text.Json;
using JobAgents.Api;
using JobAgents.Application;
using JobAgents.Application.Abstractions;
using JobAgents.Application.JobHunt;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.DependencyInjection;
using JobAgents.Infrastructure.Feedback;
using JobAgents.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Application + Infrastructure: identical wiring to the Blazor app (Coordinator, agents, event bus,
// Tavily search, pricing). The API is just a second front door onto the same domain.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// Results store rooted at the SAME location the Blazor app uses, so both UIs read/write one set of
// runs, profiles, settings and ideas. Override with the ResultsDirectory config key for tests/CI.
var resultsDir = builder.Configuration["ResultsDirectory"] is { Length: > 0 } configured
    ? configured
    : Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JobAgents", "results");

builder.Services.AddSingleton(_ => new RunStore(resultsDir));
builder.Services.AddSingleton(_ => new FeedbackStore(resultsDir));
builder.Services.AddSingleton(_ => new ProfileStore(resultsDir));
builder.Services.AddSingleton(_ => new ModelConfigStore(resultsDir));
builder.Services.AddSingleton(_ => new IdeaStore(resultsDir));
builder.Services.AddSingleton<JobAgents.Infrastructure.Sourcing.IPostingStore>(
    _ => new JobAgents.Infrastructure.Sourcing.FilePostingStore(resultsDir));
builder.Services.AddSingleton(
    _ => new JobAgents.Infrastructure.Plugins.TavilySearchCache(Path.Join(resultsDir, "tavily-cache")));
builder.Services.AddSingleton<ResumeTextExtractor>();

// CORS so the Vite dev server (and a built preview) can call the API from another origin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// ──────────────────────────────────────────────────────────────────────────────────────────────
// Job hunt — streaming (Server-Sent Events). Each agent event is one `data:` frame whose payload is
// the event serialised by its runtime type (so the stable `kind` discriminator + all fields ship).
// ──────────────────────────────────────────────────────────────────────────────────────────────
app.MapPost("/api/hunt/run", async (
    HuntRequest request,
    StartJobHuntUseCase jobHunt,
    RunStore runStore,
    ModelConfigStore modelConfigStore,
    HttpContext http,
    CancellationToken ct) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";

    async Task Send(string @event, string data)
    {
        await http.Response.WriteAsync($"event: {@event}\ndata: {data}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    var inputs = request.Inputs;
    var preferences = HuntConfigFactory.BuildPreferences(inputs);
    var title = HuntConfigFactory.BuildTitle(inputs);
    var models = await modelConfigStore.LoadAsync(ct) ?? new AgentModelConfig();
    var config = HuntConfigFactory.BuildConfig(inputs, models, request.SearchBoost);

    // Best-effort: remember the CV for next time, like the Blazor home page does.
    // (Profile save is intentionally omitted here — the explicit Save CV endpoint owns that.)

    JobHuntResult? result = null;
    decimal totalCost = 0m;

    try
    {
        var (_, events) = jobHunt.Start(request.Resume, preferences, config, inputs.Criteria, ct);

        await foreach (var evt in events.WithCancellation(ct))
        {
            await Send(evt.Kind, JsonSerializer.Serialize(evt, evt.GetType(), json));

            if (evt is AgentFinishedEvent finished)
            {
                totalCost += finished.EstimatedCostUsd ?? 0m;
                if (finished.AgentId == AgentId.System)
                {
                    try { result = JsonSerializer.Deserialize<JobHuntResult>(finished.FinalText, json); }
                    catch (JsonException) { /* leave null; client shows the error */ }
                }
            }
        }

        // Persist the finished run server-side (mirrors the Blazor app) so it appears in Past Runs.
        if (result is not null)
        {
            var run = new PersistedRun(
                RunId.New().Value, DateTime.UtcNow, title, preferences, inputs, totalCost, result);
            try { await runStore.SaveAsync(run, CancellationToken.None); } catch (Exception) { /* best-effort */ }
            await Send("run.saved", JsonSerializer.Serialize(new { run.RunId }, json));
        }

        await Send("done", "{}");
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    catch (Exception ex)
    {
        await Send("fatal", JsonSerializer.Serialize(new { message = ex.Message }, json));
    }
});

// Parse-only: resume + preferences → criteria for review/editing before a search.
app.MapPost("/api/hunt/analyze", async (AnalyzeRequest req, StartJobHuntUseCase jobHunt, CancellationToken ct) =>
    Results.Ok(await jobHunt.AnalyzeAsync(req.Resume, req.Preferences, null, ct)));

// On-demand research for one already-matched posting.
app.MapPost("/api/hunt/expand/{piece}", async (
    string piece, ExpandRequest req, IMatchExpander expander, CancellationToken ct) =>
{
    var cfg = JobHuntConfig.Default;
    var criteria = req.Criteria ?? SearchCriteria.Empty;
    return piece.ToLowerInvariant() switch
    {
        "company" => Results.Ok(await expander.ResearchCompanyAsync(req.Match, cfg, ct)),
        "salary" => Results.Ok(await expander.ResearchSalaryAsync(req.Match, criteria, cfg, ct)),
        "interview" => Results.Ok(await expander.ResearchInterviewAsync(req.Match, cfg, ct)),
        _ => Results.BadRequest(new { message = $"Unknown research piece '{piece}'." }),
    };
});

// Standalone JD gap analysis (resume vs one pasted job description).
app.MapPost("/api/jd/analyze", async (JdAnalyzeRequest req, IJdAnalysisAgent analyzer, CancellationToken ct) =>
    Results.Ok(await analyzer.AnalyzeAsync(RunId.New(), req.ResumeText, req.JobDescription, null, ct)));

// ── Resume file → text ──────────────────────────────────────────────────────────────────────────
app.MapPost("/api/extract", async (HttpRequest http, ResumeTextExtractor extractor) =>
{
    if (!http.HasFormContentType || http.Form.Files.Count == 0)
        return Results.BadRequest(new { message = "Upload a file in a multipart/form-data 'file' field." });

    var file = http.Form.Files[0];
    if (!extractor.IsSupported(file.FileName))
        return Results.BadRequest(new { message = $"Unsupported file type. Use {string.Join(", ", ResumeTextExtractor.SupportedExtensions)}." });

    await using var stream = file.OpenReadStream();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);
    buffer.Position = 0;
    var text = extractor.Extract(file.FileName, buffer);
    return string.IsNullOrWhiteSpace(text)
        ? Results.BadRequest(new { message = "No text could be extracted from that file." })
        : Results.Ok(new { text });
}).DisableAntiforgery();

// ── Past runs ─────────────────────────────────────────────────────────────────────────────────
app.MapGet("/api/runs", async (RunStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapDelete("/api/runs", async (RunStore store, CancellationToken ct) => { await store.DeleteAllAsync(ct); return Results.NoContent(); });
app.MapDelete("/api/runs/{id}", async (string id, RunStore store, CancellationToken ct) => { await store.DeleteAsync(id, ct); return Results.NoContent(); });
app.MapPut("/api/runs/{id}/rename", async (string id, RenameRequest req, RunStore store, CancellationToken ct) => { await store.RenameAsync(id, req.Title, ct); return Results.NoContent(); });
app.MapPut("/api/runs/{id}/pin", async (string id, PinRequest req, RunStore store, CancellationToken ct) => { await store.SetPinnedAsync(id, req.Pinned, ct); return Results.NoContent(); });

app.MapGet("/export/runs", async (RunStore store, CancellationToken ct) =>
{
    var runs = await store.LoadAllAsync(ct);
    var payload = JsonSerializer.Serialize(runs, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    return Results.File(Encoding.UTF8.GetBytes(payload), "application/json", "jobagents-runs.json");
});

// ── Improvement ideas (roadmap) ─────────────────────────────────────────────────────────────────
app.MapGet("/api/ideas", async (IdeaStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/ideas/statuses", () => Results.Ok(IdeaStore.Statuses));
app.MapPost("/api/ideas", async (IdeaUpsertRequest req, IdeaStore store, CancellationToken ct) => Results.Ok(await store.AddAsync(req.Title, req.Description, ct)));
app.MapPut("/api/ideas/{id}", async (string id, IdeaUpsertRequest req, IdeaStore store, CancellationToken ct) => { await store.UpdateAsync(id, req.Title, req.Description, ct); return Results.NoContent(); });
app.MapPut("/api/ideas/{id}/status", async (string id, IdeaStatusRequest req, IdeaStore store, CancellationToken ct) => { await store.UpdateStatusAsync(id, req.Status, ct); return Results.NoContent(); });
app.MapDelete("/api/ideas/{id}", async (string id, IdeaStore store, CancellationToken ct) => { await store.DeleteAsync(id, ct); return Results.NoContent(); });

// ── Candidate profile (saved CV) ────────────────────────────────────────────────────────────────
app.MapGet("/api/profile", async (ProfileStore store, CancellationToken ct) =>
    await store.LoadAsync(ct) is { } p ? Results.Ok(p) : Results.NoContent());
app.MapPost("/api/profile", async (SaveProfileRequest req, ProfileStore store, CancellationToken ct) => { await store.SaveAsync(req.ResumeText, ct); return Results.NoContent(); });
app.MapDelete("/api/profile", async (ProfileStore store) => { await store.DeleteAsync(); return Results.NoContent(); });

// ── Settings (per-agent model config) ───────────────────────────────────────────────────────────
app.MapGet("/api/settings", async (ModelConfigStore store, CancellationToken ct) =>
    Results.Ok(await store.LoadAsync(ct) ?? new AgentModelConfig()));
app.MapPut("/api/settings", async (AgentModelConfig config, ModelConfigStore store, CancellationToken ct) => { await store.SaveAsync(config, ct); return Results.NoContent(); });
app.MapGet("/api/settings/catalog", () => Results.Ok(ModelCatalog.Options));

// ── Feedback (human match scores) ───────────────────────────────────────────────────────────────
app.MapPost("/api/feedback", async (FeedbackRequest req, FeedbackStore store, CancellationToken ct) =>
{
    var feedback = new MatchFeedback(
        RunId: req.RunId,
        CreatedAtUtc: DateTime.UtcNow,
        Resume: req.Resume,
        Posting: req.Posting,
        Criteria: req.Criteria,
        AgentScore: req.AgentScore,
        AgentMatchedSkills: req.AgentMatchedSkills,
        HumanScore: req.HumanScore,
        Note: req.Note);
    try { await store.SaveAsync(feedback, ct); } catch { /* best-effort */ }
    return Results.NoContent();
});

app.MapGet("/", () => Results.Ok(new { service = "JobAgents.Api", status = "ok" }));

app.Run();
