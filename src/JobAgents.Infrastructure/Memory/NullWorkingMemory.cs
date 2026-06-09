using JobAgents.Application.Abstractions;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Memory;

/// <summary>No-op working memory. v1 runs without a vector store; the port exists for later.</summary>
public sealed class NullWorkingMemory : IWorkingMemory
{
    public Task RememberAsync(RunId runId, string key, string content, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> RecallAsync(RunId runId, string query, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
