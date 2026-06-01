using FluentAssertions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.EventBus;

namespace JobAgents.Infrastructure.Tests.EventBus;

public class ChannelAgentEventBusTests
{
    [Fact]
    public async Task SubscribeAsync_returns_events_for_the_matching_run_only()
    {
        var bus = new ChannelAgentEventBus();
        var runA = new RunId("run-a");
        var runB = new RunId("run-b");

        var subscription = Task.Run(async () =>
        {
            var received = new List<string>();
            await foreach (var evt in bus.SubscribeAsync(runA))
                if (evt is AgentTokenEvent t)
                    received.Add(t.Delta);
            return received;
        });

        await bus.PublishAsync(new AgentTokenEvent(runA, AgentId.Search, "A1", DateTime.UtcNow));
        await bus.PublishAsync(new AgentTokenEvent(runB, AgentId.Search, "B1", DateTime.UtcNow));
        await bus.PublishAsync(new AgentTokenEvent(runA, AgentId.Search, "A2", DateTime.UtcNow));
        await bus.PublishAsync(new AgentFinishedEvent(runA, AgentId.System, "done", 0, 0, null, DateTime.UtcNow));

        var received = await subscription;

        received.Should().ContainInOrder("A1", "A2");
        received.Should().NotContain("B1");
    }

    [Fact]
    public async Task SubscribeAsync_completes_on_system_finished()
    {
        var bus = new ChannelAgentEventBus();
        var runId = new RunId("run");

        await bus.PublishAsync(new AgentStartedEvent(runId, AgentId.Coordinator, "Coordinator", DateTime.UtcNow));
        await bus.PublishAsync(new AgentFinishedEvent(runId, AgentId.System, "done", 0, 0, null, DateTime.UtcNow));
        // This event is published after the terminal one and must NOT be observed.
        await bus.PublishAsync(new AgentTokenEvent(runId, AgentId.Search, "late", DateTime.UtcNow));

        var count = 0;
        await foreach (var _ in bus.SubscribeAsync(runId))
            count++;

        count.Should().Be(2);
    }
}
