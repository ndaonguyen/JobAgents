using System.Text.Json;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// The Coordinator. Owns the end-to-end job-hunt pipeline and the terminal System event:
/// parse criteria → search → match (parallel) → rank/select top-N → expand each (company /
/// salary / interview prep, parallel) → synthesise. Specialist agents produce data; the
/// Coordinator publishes the domain events and aggregates token usage.
/// </summary>
public sealed class Coordinator(
    IAgentRunner runner,
    ISearchAgent searchAgent,
    IResumeMatchAgent resumeMatchAgent,
    ICompanyResearchAgent companyResearchAgent,
    ISalaryAnalysisAgent salaryAnalysisAgent,
    IInterviewPrepAgent interviewPrepAgent,
    IAgentEventBus bus,
    AgentRunContext context,
    RunUsageAccumulator usageAccumulator,
    Sourcing.IPostingStore postingStore,
    ILogger<Coordinator> logger)
    : IOrchestrator
{
    public async Task RunAsync(AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default)
    {
        var runId = request.RunId;

        // Make the run's source + recency filters available to the web-search plugin (flows via AsyncLocal).
        context.IncludeDomains = config.IncludeDomains ?? Array.Empty<string>();
        context.TimeRange = config.TimeRange;
        context.StartDate = config.StartDate;
        context.EndDate = config.EndDate;

        // 1. Parse criteria — unless the caller already supplied (user-edited) criteria.
        var criteria = request.Criteria ?? await ParseCriteriaCoreAsync(request, config, ct);

        // 2. Source postings: first REUSE fresh, criteria-matching rows from the corpus (no Tavily),
        // then top up with a live search only for the remaining gap. Newly-found postings are saved
        // back so future runs can reuse them.
        var cached = postingStore.Query(criteria, config.TimeRange, config.MaxResults);
        IReadOnlyList<JobPosting> fresh = Array.Empty<JobPosting>();
        if (cached.Count < config.MaxResults)
        {
            fresh = await searchAgent.FindJobsAsync(runId, criteria, config, ct);
            await postingStore.SaveAsync(fresh, ct);
        }
        else
        {
            logger.LogInformation("Run {RunId}: served {Count} postings from cache; skipped live search.", runId, cached.Count);
        }

        var postings = Dedupe(cached.Concat(fresh).ToList()).Take(config.MaxResults).ToList();
        await bus.PublishAsync(new JobsFoundEvent(runId, AgentId.Search, postings, DateTime.UtcNow), ct);

        // 3. Match every posting in parallel (concurrency-gated).
        var matches = await FanOutAsync(
            postings,
            config.MaxFanOutConcurrency,
            async (posting, index) =>
            {
                var match = await resumeMatchAgent.MatchAsync(runId, index, request.ResumeText, posting, criteria, config, ct);
                await bus.PublishAsync(new JobMatchedEvent(runId, AgentId.ResumeMatch(index), match, DateTime.UtcNow), ct);
                return match;
            },
            ct);

        // 4. Keep every match that clears the score bar, ranked best-first.
        var qualifying = matches
            .Where(m => m.Score >= config.MinMatchScore)
            .OrderByDescending(m => m.Score)
            .ToList();

        // 5. Fully research only the top matches (company + salary + interview prep, in parallel)…
        // Company research is deduped by company name: two top matches at the same employer share one
        // research call (and one set of Tavily requests) instead of repeating it.
        var toExpand = qualifying.Take(config.TopMatchesToExpand).ToList();
        var companyResearch = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<CompanyInsight?>>>(
            StringComparer.OrdinalIgnoreCase);
        var expanded = await FanOutAsync(
            toExpand,
            config.MaxFanOutConcurrency,
            async (match, index) => await ExpandAsync(runId, index, match, criteria, config, companyResearch, ct),
            ct);

        // …and include the remaining qualifying matches as match-only dossiers (cheap, no extra agents).
        var rest = qualifying
            .Skip(config.TopMatchesToExpand)
            .Select(m => new JobDossier(m, null, null, null));

        var dossiers = expanded.Concat(rest).ToList();

        // 6. Synthesise a short summary.
        var summary = await SynthesiseAsync(runId, criteria, dossiers, config, ct);

        var result = new JobHuntResult(criteria, dossiers, summary);

        // 7. Terminal System event carries the structured result + the TRUE run total (every agent's
        // usage, recorded by the AgentRunner), not just the Coordinator's own calls.
        var usage = usageAccumulator.Take(runId);
        var finalJson = JsonSerializer.Serialize(result, AgentJson.Options);
        await bus.PublishAsync(
            new AgentFinishedEvent(
                runId, AgentId.System, finalJson,
                usage.TokensIn, usage.TokensOut, usage.EstimatedCostUsd, DateTime.UtcNow),
            ct);

        logger.LogInformation(
            "Run {RunId} finished: {Postings} postings, {Top} expanded dossiers",
            runId, postings.Count, dossiers.Count);
    }

    /// <summary>Parses the candidate's resume + preferences into structured criteria (no search run).</summary>
    public async Task<SearchCriteria> ParseCriteriaAsync(
        AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default)
    {
        var criteria = await ParseCriteriaCoreAsync(request, config, ct);
        // Standalone preview (no terminal System event): release the tally so it can't accumulate.
        usageAccumulator.Take(request.RunId);
        return criteria;
    }

    private async Task<SearchCriteria> ParseCriteriaCoreAsync(
        AgentRunRequest request, JobHuntConfig config, CancellationToken ct)
    {
        const string systemPrompt =
            """
            You are the coordinator of a job hunt. Turn the candidate's resume and preferences into
            structured search criteria. Return ONLY a JSON object:
            {
              "roles": string[],
              "locations": string[],
              "seniority": string,
              "mustHaveSkills": string[],
              "niceToHaveSkills": string[],
              "workStyles": string[],
              "salaryExpectation": string or null
            }
            "workStyles" is any of "Onsite", "Hybrid", "Remote" the candidate would accept (include all
            that apply; empty means no preference). Infer sensible values from the resume when
            preferences are vague.
            """;

        var userPrompt =
            $"""
            RESUME:
            {request.ResumeText}

            PREFERENCES:
            {request.Preferences}
            """;

        var result = await runner.RunAsync(
            runId: request.RunId, AgentId.Coordinator, "Coordinator",
            systemPrompt, userPrompt, config.CoordinatorModel, useTools: false, ct, jsonMode: true);

        return AgentJson.TryParse<SearchCriteria>(result.Text) ?? SearchCriteria.Empty;
    }

    private async Task<JobDossier> ExpandAsync(
        RunId runId, int index, JobMatch match, SearchCriteria criteria, JobHuntConfig config,
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<CompanyInsight?>>> companyResearch,
        CancellationToken ct)
    {
        var posting = match.Posting;

        // Research each distinct company once; same-company matches await the shared task.
        var company = (posting.Company ?? string.Empty).Trim();
        var companyTask = companyResearch.GetOrAdd(
            company,
            _ => new Lazy<Task<CompanyInsight?>>(() => Run(
                () => companyResearchAgent.ResearchAsync(runId, index, company, config, ct),
                ev => new CompanyResearchedEvent(runId, AgentId.CompanyResearch(index), ev, DateTime.UtcNow)))).Value;
        var salaryTask = Run(() => salaryAnalysisAgent.AnalyzeAsync(runId, index, posting, criteria, config, ct),
            ev => new SalaryAnalyzedEvent(runId, AgentId.SalaryAnalysis(index), ev, DateTime.UtcNow));
        var interviewTask = Run(() => interviewPrepAgent.PrepareAsync(runId, index, posting, match, config, ct),
            ev => new InterviewPrepReadyEvent(runId, AgentId.InterviewPrep(index), ev, DateTime.UtcNow));

        await Task.WhenAll(companyTask, salaryTask, interviewTask);
        return new JobDossier(match, companyTask.Result, salaryTask.Result, interviewTask.Result);

        // Runs a specialist and publishes its domain event; failures degrade to null rather than aborting the run.
        async Task<T?> Run<T>(Func<Task<T>> work, Func<T, AgentEvent> toEvent) where T : class
        {
            try
            {
                var value = await work();
                await bus.PublishAsync(toEvent(value), ct);
                return value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Expansion step failed for match {Title}", posting.Title);
                return null;
            }
        }
    }

    private async Task<string> SynthesiseAsync(
        RunId runId, SearchCriteria criteria, IReadOnlyList<JobDossier> dossiers, JobHuntConfig config, CancellationToken ct)
    {
        if (dossiers.Count == 0)
            return $"No postings scored at or above {config.MinMatchScore}/100 for the given criteria.";

        const string systemPrompt =
            "You are the coordinator. Write a concise (3-5 sentence) summary of the candidate's best "
            + "job matches and what to focus on next. Plain prose, no JSON.";

        var lines = dossiers
            .Take(8)
            .Select(d => $"- {d.Match.Posting.Title} @ {d.Match.Posting.Company} (fit {d.Match.Score}/100)");
        var userPrompt = $"Top matches ({dossiers.Count} total above the bar):\n{string.Join('\n', lines)}";

        var result = await runner.RunAsync(
            runId, AgentId.Coordinator, "Coordinator",
            systemPrompt, userPrompt, config.CoordinatorModel, useTools: false, ct);

        return result.Text;
    }

    internal static IReadOnlyList<JobPosting> Dedupe(IReadOnlyList<JobPosting> postings) =>
        postings
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Url) ? $"{p.Title}|{p.Company}" : p.Url,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>Runs <paramref name="work"/> over each item with bounded concurrency, preserving input order.</summary>
    private static async Task<IReadOnlyList<TResult>> FanOutAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        int maxConcurrency,
        Func<TItem, int, Task<TResult>> work,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return Array.Empty<TResult>();

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await work(item, index);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }
}
