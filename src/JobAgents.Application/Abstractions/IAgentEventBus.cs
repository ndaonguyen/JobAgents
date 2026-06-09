using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// In-process pub/sub for run events. Each run has an isolated stream; the stream completes when
/// the run emits a System-level <see cref="AgentFinishedEvent"/>/<see cref="AgentErrorEvent"/> or
/// the cancellation token fires.
/// </summary>
public interface IAgentEventBus
{
    ValueTask PublishAsync(AgentEvent evt, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> SubscribeAsync(RunId runId, CancellationToken ct = default);
}
