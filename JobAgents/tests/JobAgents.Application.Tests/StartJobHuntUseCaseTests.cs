using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Application.JobHunt;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Infrastructure.EventBus;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAgents.Application.Tests;

public class StartJobHuntUseCaseTests
{
    [Fact]
    public async Task Start_streams_events_published_by_the_orchestrator()
    {
        var bus = new ChannelAgentEventBus();
        var orchestrator = new FakeOrchestrator(async (req, ct) =>
        {
            await bus.PublishAsync(new AgentStartedEvent(req.RunId, AgentId.Coordinator, "Coordinator", DateTime.UtcNow), ct);
            await bus.PublishAsync(new AgentFinishedEvent(req.RunId, AgentId.System, "{}", 10, 20, 0.01m, DateTime.UtcNow), ct);
        });
        var useCase = new StartJobHuntUseCase(orchestrator, bus, NullLogger<StartJobHuntUseCase>.Instance);

        var (_, events) = useCase.Start("resume", "prefs");

        var collected = new List<AgentEvent>();
        await foreach (var evt in events)
            collected.Add(evt);

        collected.Should().HaveCount(2);
        collected[0].Should().BeOfType<AgentStartedEvent>();
        collected[^1].Should().BeOfType<AgentFinishedEvent>()
            .Which.AgentId.Should().Be(AgentId.System);
    }

    [Fact]
    public async Task Start_publishes_a_terminal_error_when_the_orchestrator_throws()
    {
        var bus = new ChannelAgentEventBus();
        var orchestrator = new FakeOrchestrator((_, _) => throw new InvalidOperationException("kaboom"));
        var useCase = new StartJobHuntUseCase(orchestrator, bus, NullLogger<StartJobHuntUseCase>.Instance);

        var (_, events) = useCase.Start("resume", "prefs");

        var collected = new List<AgentEvent>();
        await foreach (var evt in events)
            collected.Add(evt);

        collected.Should().ContainSingle()
            .Which.Should().BeOfType<AgentErrorEvent>()
            .Which.Message.Should().Be("kaboom");
    }

    private sealed class FakeOrchestrator(Func<Domain.Agents.AgentRunRequest, CancellationToken, Task> body)
        : IOrchestrator
    {
        public Task RunAsync(Domain.Agents.AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default)
            => body(request, ct);
    }
}
