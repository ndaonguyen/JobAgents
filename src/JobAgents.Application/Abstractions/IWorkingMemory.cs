using JobAgents.Domain.Runs;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// Optional per-run scratch memory (e.g. a vector store). v1 ships only a no-op implementation;
/// the port exists so a real store can be slotted in later without touching the Coordinator.
/// </summary>
public interface IWorkingMemory
{
    Task RememberAsync(RunId runId, string key, string content, CancellationToken ct = default);

    Task<IReadOnlyList<string>> RecallAsync(RunId runId, string query, int limit, CancellationToken ct = default);
}
