using System.Text.Json;

namespace JobAgents.Web.Services;

/// <summary>A single actionable sub-task that belongs to an <see cref="ImprovementIdea"/> (story).</summary>
public sealed record SubTask(
    string Id,
    string Title,
    bool Done);

/// <summary>
/// A single improvement idea / future-work item (a "story") for engineers to pick up and build.
/// May be broken down into <see cref="SubTasks"/>.
/// </summary>
public sealed record ImprovementIdea(
    string Id,
    string Title,
    string Description,
    string Status,
    DateTime CreatedAtUtc)
{
    /// <summary>Acceptance criteria / definition of done for the story. Empty for legacy items.</summary>
    public string Criteria { get; init; } = string.Empty;

    /// <summary>Sub-tasks this story is broken down into. Empty for legacy items.</summary>
    public IReadOnlyList<SubTask> SubTasks { get; init; } = [];
}

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

    public async Task<ImprovementIdea> AddAsync(
        string title, string description, string criteria = "", CancellationToken ct = default)
    {
        var idea = new ImprovementIdea(
            Guid.NewGuid().ToString("N"), title.Trim(), description.Trim(), Statuses[0], DateTime.UtcNow)
        {
            Criteria = criteria.Trim(),
        };
        await MutateAsync(list => list.Insert(0, idea), ct);
        return idea;
    }

    public Task UpdateAsync(
        string id, string title, string description, string criteria = "", CancellationToken ct = default) =>
        MutateAsync(list =>
        {
            var i = list.FindIndex(x => x.Id == id);
            if (i >= 0)
                list[i] = list[i] with
                {
                    Title = title.Trim(),
                    Description = description.Trim(),
                    Criteria = criteria.Trim(),
                };
        }, ct);

    public Task AddSubTaskAsync(string ideaId, string title, CancellationToken ct = default) =>
        MutateIdeaAsync(ideaId, idea => idea with
        {
            SubTasks = [.. idea.SubTasks, new SubTask(Guid.NewGuid().ToString("N"), title.Trim(), false)],
        }, ct);

    public Task ToggleSubTaskAsync(string ideaId, string subTaskId, CancellationToken ct = default) =>
        MutateIdeaAsync(ideaId, idea => idea with
        {
            SubTasks = [.. idea.SubTasks.Select(s => s.Id == subTaskId ? s with { Done = !s.Done } : s)],
        }, ct);

    public Task DeleteSubTaskAsync(string ideaId, string subTaskId, CancellationToken ct = default) =>
        MutateIdeaAsync(ideaId, idea => idea with
        {
            SubTasks = [.. idea.SubTasks.Where(s => s.Id != subTaskId)],
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

    private Task MutateIdeaAsync(string id, Func<ImprovementIdea, ImprovementIdea> transform, CancellationToken ct) =>
        MutateAsync(list =>
        {
            var i = list.FindIndex(x => x.Id == id);
            if (i >= 0)
                list[i] = transform(list[i]);
        }, ct);

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
