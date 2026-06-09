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
    WebSearchAccumulator searchCounts,
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
        context.SearchDepth = config.SearchDepth ?? SearchDepthSettings.Default;
        context.MaxSearchResultChars = config.MaxSearchResultChars;

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

        // Hard seniority gate: drop postings clearly BELOW the requested level. The cache already
        // applies this on its own query, but the live search does not — so without this a freshly
        // searched Senior role would slip through a Lead/Staff filter (it was only soft-capped by the
        // matcher before). Title-based + description fallback; Unknown levels pass (lenient).
        var seniorityFloor = Seniority.Parse(criteria.Seniority);
        var postings = Dedupe(cached.Concat(fresh).ToList())
            .Where(p => !Seniority.IsBelowFloor(p, seniorityFloor))
            .Take(config.MaxResults)
            .ToList();
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
        // Salary depends on role + location + seniority, NOT the employer, so two top matches for the
        // same role in the same place share one lookup (and its Tavily calls) instead of repeating the
        // identical market-data search. Same dedupe pattern as companyResearch above.
        var salaryResearch = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<SalaryEstimate?>>>(
            StringComparer.OrdinalIgnoreCase);
        var expanded = await FanOutAsync(
            toExpand,
            config.MaxFanOutConcurrency,
            async (match, index) => await ExpandAsync(runId, index, match, criteria, config, companyResearch, salaryResearch, ct),
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
        // Report where the run's live web-search budget actually went, broken down by agent.
        logger.LogInformation("Run {RunId} web search: {Breakdown}", runId, searchCounts.TakeSummary(runId));
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

            IMPORTANT — the PREFERENCES describe the candidate's TARGET: the roles and level they want
            NEXT, which may be ABOVE their current resume level. When the preferences name target roles
            or a seniority, use THOSE for "roles" and "seniority" — do NOT downgrade them to the level
            shown in the resume. Map the target roles to the matching seniority (e.g. "Tech Lead" or
            "Staff Engineer" → "Lead"; "Principal" → "Principal"; "Engineering Manager" → "Manager").
            Only infer roles/seniority from the resume when the preferences don't state a target. Example:
            a Senior-level resume with target roles "Staff Engineer, Tech Lead" → "seniority": "Lead" and
            "roles": ["Staff Engineer", "Tech Lead"] — NOT "Senior".
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
        System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<SalaryEstimate?>>> salaryResearch,
        CancellationToken ct)
    {
        var posting = match.Posting;

        // Research each distinct company once; same-company matches await the shared task. Opt-in:
        // skipped entirely (no agent, no Tavily calls) unless the run enabled company research.
        var company = (posting.Company ?? string.Empty).Trim();
        var companyTask = config.ResearchCompany
            ? companyResearch.GetOrAdd(
                company,
                _ => new Lazy<Task<CompanyInsight?>>(() => Run(
                    () => companyResearchAgent.ResearchAsync(runId, index, company, config, ct),
                    ev => new CompanyResearchedEvent(runId, AgentId.CompanyResearch(index), ev, DateTime.UtcNow)))).Value
            : Task.FromResult<CompanyInsight?>(null);
        // Analyse each distinct role+location+seniority once; market rate is company-independent, so
        // matches that share those three await the same task instead of re-searching for the range.
        // Opt-in: skipped entirely (no agent, no Tavily calls) unless the run enabled salary research.
        var salaryKey = string.Join('|',
            (posting.Title ?? string.Empty).Trim().ToLowerInvariant(),
            (posting.Location ?? string.Empty).Trim().ToLowerInvariant(),
            (criteria.Seniority ?? string.Empty).Trim().ToLowerInvariant());
        var salaryTask = config.ResearchSalary
            ? salaryResearch.GetOrAdd(
                salaryKey,
                _ => new Lazy<Task<SalaryEstimate?>>(() => Run(
                    () => salaryAnalysisAgent.AnalyzeAsync(runId, index, posting, criteria, config, ct),
                    ev => new SalaryAnalyzedEvent(runId, AgentId.SalaryAnalysis(index), ev, DateTime.UtcNow)))).Value
            : Task.FromResult<SalaryEstimate?>(null);
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

    /// <summary>
    /// Collapses duplicate postings in two passes, keeping the first occurrence each time:
    /// (1) by canonical URL — folds tracking/query-param and slug variants of one listing together (and
    /// handles a reposted listing whose title changed but whose URL did not);
    /// (2) by normalised title+company signature — folds the same job that arrived under different URLs
    /// or a slightly different company string ("CodeHQ" vs "CodeHQ Vietnam"). Job boards routinely
    /// surface one opening under several distinct URLs, which pass 1 alone can't catch.
    /// Postings without a URL fall through pass 1 keyed by their signature, so pass 2 is idempotent for
    /// them. Note: this also folds genuinely-distinct same-title roles at one employer into one card.
    /// </summary>
    internal static IReadOnlyList<JobPosting> Dedupe(IReadOnlyList<JobPosting> postings) =>
        postings
            .GroupBy(p =>
            {
                var url = Sourcing.PostingKey.CanonicalUrl(p.Url);
                return url.Length > 0 ? url : Sourcing.PostingKey.Signature(p);
            }, StringComparer.Ordinal)
            .Select(g => g.First())
            .GroupBy(Sourcing.PostingKey.Signature, StringComparer.Ordinal)
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
