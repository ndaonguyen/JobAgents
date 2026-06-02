using System.Text.Json;

namespace JobAgents.Web.Services;

/// <summary>A single improvement idea / future-work item for engineers to pick up and build.</summary>
public sealed record ImprovementIdea(
    string Id,
    string Title,
    string Description,
    string Status,
    DateTime CreatedAtUtc);

/// <summary>
/// File-backed backlog of improvement ideas, stored as <c>results/ideas.json</c>. Lets the team
/// capture future-work items (title + description + status) from the UI. Best-effort, single-file
/// storage guarded by a lock so concurrent edits don't corrupt the file.
/// </summary>
public sealed class IdeaStore(string directory)
{
    /// <summary>Allowed workflow statuses, in order.</summary>
    public static readonly string[] Statuses = ["Proposed", "Planned", "In Progress", "Done"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string _file = Path.Combine(directory, "ideas.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<ImprovementIdea>> LoadAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await ReadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ImprovementIdea> AddAsync(string title, string description, CancellationToken ct = default)
    {
        var idea = new ImprovementIdea(
            Guid.NewGuid().ToString("N"), title.Trim(), description.Trim(), Statuses[0], DateTime.UtcNow);
        await MutateAsync(list => list.Insert(0, idea), ct);
        return idea;
    }

    public Task UpdateAsync(string id, string title, string description, CancellationToken ct = default) =>
        MutateAsync(list =>
        {
            var i = list.FindIndex(x => x.Id == id);
            if (i >= 0)
                list[i] = list[i] with { Title = title.Trim(), Description = description.Trim() };
        }, ct);

    public Task UpdateStatusAsync(string id, string status, CancellationToken ct = default) =>
        MutateAsync(list =>
        {
            var i = list.FindIndex(x => x.Id == id);
            if (i >= 0)
                list[i] = list[i] with { Status = status };
        }, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        MutateAsync(list => list.RemoveAll(x => x.Id == id), ct);

    private async Task MutateAsync(Action<List<ImprovementIdea>> mutate, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAsync(ct);
            mutate(list);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(list, JsonOptions), ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ImprovementIdea>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_file))
            return new();
        try
        {
            var json = await File.ReadAllTextAsync(_file, ct);
            return JsonSerializer.Deserialize<List<ImprovementIdea>>(json, JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
