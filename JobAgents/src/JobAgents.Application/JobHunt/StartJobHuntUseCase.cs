using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace JobAgents.Application.JobHunt;

/// <summary>
/// Entry point for a job-hunt run. Subscribes to the run's event stream *before* kicking off the
/// Coordinator (so no early events are missed), runs the Coordinator on a background task, and
/// turns any unhandled failure into a terminal System-level error event.
/// </summary>
public sealed class StartJobHuntUseCase(
    IOrchestrator orchestrator,
    IAgentEventBus bus,
    ILogger<StartJobHuntUseCase> logger)
{
    public (RunId RunId, IAsyncEnumerable<AgentEvent> Events) Start(
        string resumeText,
        string preferences,
        JobHuntConfig? config = null,
        CancellationToken ct = default)
    {
        var runId = RunId.New();
        var events = bus.SubscribeAsync(runId, ct);
        var request = new AgentRunRequest(runId, resumeText, preferences);

        _ = Task.Run(async () =>
        {
            try
            {
                await orchestrator.RunAsync(request, config ?? JobHuntConfig.Default, ct);
            }
            catch (OperationCanceledException)
            {
                // Caller cancelled (e.g. browser disconnected); nothing to report.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job-hunt run {RunId} failed", runId);
                await bus.PublishAsync(
                    new AgentErrorEvent(runId, AgentId.System, ex.Message, DateTime.UtcNow),
                    CancellationToken.None);
            }
        }, ct);

        return (runId, events);
    }
}
