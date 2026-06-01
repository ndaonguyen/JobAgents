using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Expands one match on demand by running the company / salary / interview specialists in parallel.
/// Uses a throwaway run id; its agent events are drained and discarded (the UI just awaits the
/// result), and the bus channel is cleaned up via a terminal System event.
/// </summary>
public sealed class MatchExpander(
    ICompanyResearchAgent companyResearchAgent,
    ISalaryAnalysisAgent salaryAnalysisAgent,
    IInterviewPrepAgent interviewPrepAgent,
    IAgentEventBus bus,
    AgentRunContext context)
    : IMatchExpander
{
    public async Task<JobDossier> ExpandAsync(
        JobMatch match, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct = default)
    {
        var runId = RunId.New();
        context.IncludeDomains = config.IncludeDomains ?? Array.Empty<string>();

        // Drain this run's events in the background so the channel is removed when we finish.
        var drain = Task.Run(async () =>
        {
            await foreach (var _ in bus.SubscribeAsync(runId, ct))
            {
                // discard — the UI awaits the returned dossier instead of streaming.
            }
        }, ct);

        try
        {
            var posting = match.Posting;
            var companyTask = companyResearchAgent.ResearchAsync(runId, 0, posting.Company, config, ct);
            var salaryTask = salaryAnalysisAgent.AnalyzeAsync(runId, 0, posting, criteria, config, ct);
            var interviewTask = interviewPrepAgent.PrepareAsync(runId, 0, posting, match, config, ct);

            await Task.WhenAll(companyTask, salaryTask, interviewTask);
            return new JobDossier(match, companyTask.Result, salaryTask.Result, interviewTask.Result);
        }
        finally
        {
            // Close the stream so the background drain completes and tidies up the channel.
            await bus.PublishAsync(
                new AgentFinishedEvent(runId, AgentId.System, string.Empty, 0, 0, null, DateTime.UtcNow),
                CancellationToken.None);
            try { await drain; } catch { /* cancellation/teardown — ignore */ }
        }
    }
}
