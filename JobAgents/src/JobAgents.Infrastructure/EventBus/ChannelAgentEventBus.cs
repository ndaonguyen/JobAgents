using System.Collections.Concurrent;
using System.Threading.Channels;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.EventBus;

/// <summary>
/// In-process event bus with one unbounded channel per run, so concurrent runs are fully isolated.
/// A subscriber's stream completes when the run emits a System-level finished/error event; the
/// channel is then removed in the subscriber's <c>finally</c> block.
/// </summary>
public sealed class ChannelAgentEventBus : IAgentEventBus
{
    private readonly ConcurrentDictionary<string, Channel<AgentEvent>> _channels = new();

    public ValueTask PublishAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var channel = _channels.GetOrAdd(evt.RunId.Value, _ => CreateChannel());
        return channel.Writer.WriteAsync(evt, ct);
    }

    public async IAsyncEnumerable<AgentEvent> SubscribeAsync(
        RunId runId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = _channels.GetOrAdd(runId.Value, _ => CreateChannel());
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
                if (IsTerminal(evt))
                    yield break;
            }
        }
        finally
        {
            _channels.TryRemove(runId.Value, out _);
        }
    }

    private static Channel<AgentEvent> CreateChannel() =>
        Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private static bool IsTerminal(AgentEvent evt) =>
        evt.AgentId == AgentId.System && evt is AgentFinishedEvent or AgentErrorEvent;
}
