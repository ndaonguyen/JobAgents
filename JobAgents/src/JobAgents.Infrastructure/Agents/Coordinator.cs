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
    ILogger<Coordinator> logger)
    : IOrchestrator
{
    public async Task RunAsync(AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default)
    {
        var runId = request.RunId;
        var usage = AgentUsage.Zero;

        // Make the run's source filter available to the web-search plugin (flows via AsyncLocal).
        context.IncludeDomains = config.IncludeDomains ?? Array.Empty<string>();

        // 1. Parse criteria.
        var (criteria, criteriaUsage) = await ParseCriteriaAsync(request, config, ct);
        usage = usage.Add(criteriaUsage);

        // 2. Search for postings.
        var postings = Dedupe(await searchAgent.FindJobsAsync(runId, criteria, config, ct));
        await bus.PublishAsync(new JobsFoundEvent(runId, AgentId.Search, postings, DateTime.UtcNow), ct);

        // 3. Match every posting in parallel (concurrency-gated).
        var matches = await FanOutAsync(
            postings,
            config.MaxFanOutConcurrency,
            async (posting, index) =>
            {
                var match = await resumeMatchAgent.MatchAsync(runId, index, request.ResumeText, posting, config, ct);
                await bus.PublishAsync(new JobMatchedEvent(runId, AgentId.ResumeMatch(index), match, DateTime.UtcNow), ct);
                return match;
            },
            ct);

        // 4. Rank and select the top matches to expand.
        var topMatches = matches
            .OrderByDescending(m => m.Score)
            .Take(config.TopMatchesToExpand)
            .ToList();

        // 5. Expand each top match: company + salary + interview prep, in parallel.
        var dossiers = await FanOutAsync(
            topMatches,
            config.MaxFanOutConcurrency,
            async (match, index) => await ExpandAsync(runId, index, match, criteria, config, ct),
            ct);

        // 6. Synthesise a short summary.
        var (summary, summaryUsage) = await SynthesiseAsync(runId, criteria, dossiers, config, ct);
        usage = usage.Add(summaryUsage);

        var result = new JobHuntResult(criteria, dossiers, summary);

        // 7. Terminal System event carries the structured result + aggregated usage.
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

    private async Task<(SearchCriteria, AgentUsage)> ParseCriteriaAsync(
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
              "remoteOnly": boolean,
              "salaryExpectation": string or null
            }
            Infer sensible values from the resume when preferences are vague.
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
            systemPrompt, userPrompt, config.CoordinatorModel, useTools: false, ct);

        var criteria = AgentJson.TryParse<SearchCriteria>(result.Text) ?? SearchCriteria.Empty;
        return (criteria, result.Usage);
    }

    private async Task<JobDossier> ExpandAsync(
        RunId runId, int index, JobMatch match, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
    {
        var posting = match.Posting;

        var companyTask = Run(() => companyResearchAgent.ResearchAsync(runId, index, posting.Company, config, ct),
            ev => new CompanyResearchedEvent(runId, AgentId.CompanyResearch(index), ev, DateTime.UtcNow));
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

    private async Task<(string, AgentUsage)> SynthesiseAsync(
        RunId runId, SearchCriteria criteria, IReadOnlyList<JobDossier> dossiers, JobHuntConfig config, CancellationToken ct)
    {
        if (dossiers.Count == 0)
            return ("No matching job postings were found for the given criteria.", AgentUsage.Zero);

        const string systemPrompt =
            "You are the coordinator. Write a concise (3-5 sentence) summary of the candidate's best "
            + "job matches and what to focus on next. Plain prose, no JSON.";

        var lines = dossiers.Select(d =>
            $"- {d.Match.Posting.Title} @ {d.Match.Posting.Company} (fit {d.Match.Score}/100)");
        var userPrompt = $"Top matches:\n{string.Join('\n', lines)}";

        var result = await runner.RunAsync(
            runId, AgentId.Coordinator, "Coordinator",
            systemPrompt, userPrompt, config.CoordinatorModel, useTools: false, ct);

        return (result.Text, result.Usage);
    }

    private static IReadOnlyList<JobPosting> Dedupe(IReadOnlyList<JobPosting> postings) =>
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
