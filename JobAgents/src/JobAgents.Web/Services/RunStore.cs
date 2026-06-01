using System.Text.Json;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Web.Services;

/// <summary>The structured search form inputs, stored so a past run can be reloaded and continued.</summary>
public sealed record SearchInputs(
    string[] Roles,
    string[] Languages,
    string[] WorkingStyles,
    string Location,
    int? SalaryMin,
    int? SalaryMax,
    string Currency,
    string Other,
    string[]? Sources = null,
    int MinMatchScore = 60,
    string? PostedWithin = null,
    string? StartDate = null,
    string? EndDate = null)
{
    public static SearchInputs Empty { get; } =
        new([], [], [], string.Empty, null, null, "USD", string.Empty, []);
}

/// <summary>A persisted job-hunt run, appended to a daily JSONL file and listed on /past-runs.</summary>
public sealed record PersistedRun(
    string RunId,
    DateTime CompletedAtUtc,
    string Title,
    string Preferences,
    SearchInputs? Inputs,
    decimal? EstimatedCostUsd,
    JobHuntResult Result,
    bool Pinned = false);

/// <summary>
/// Appends finished runs to <c>results/ui-{yyyyMMdd}.jsonl</c> (one JSON object per line) and reads
/// them back for the Past Runs viewer. Simple file storage — no database needed for v1.
/// </summary>
public sealed class RunStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task SaveAsync(PersistedRun run, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"ui-{run.CompletedAtUtc:yyyyMMdd}.jsonl");
        var line = JsonSerializer.Serialize(run, JsonOptions);

        await _writeLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedRun>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<PersistedRun>();

        var runs = new List<PersistedRun>();
        foreach (var file in Directory.EnumerateFiles(directory, "ui-*.jsonl"))
        {
            foreach (var line in await File.ReadAllLinesAsync(file, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    if (JsonSerializer.Deserialize<PersistedRun>(line, JsonOptions) is { } run)
                        runs.Add(run);
                }
                catch (JsonException)
                {
                    // Skip malformed lines.
                }
            }
        }

        // Pinned searches float to the top, then most-recent first.
        return runs
            .OrderByDescending(r => r.Pinned)
            .ThenByDescending(r => r.CompletedAtUtc)
            .ToList();
    }

    /// <summary>Deletes the run with the given id.</summary>
    public Task DeleteAsync(string runId, CancellationToken ct = default) =>
        MutateAsync(run => run.RunId == runId ? null : run, ct);

    /// <summary>Deletes every saved run.</summary>
    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return;

        await _writeLock.WaitAsync(ct);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "ui-*.jsonl"))
                File.Delete(file);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Renames the run's title.</summary>
    public Task RenameAsync(string runId, string title, CancellationToken ct = default) =>
        MutateAsync(run => run.RunId == runId ? run with { Title = title } : run, ct);

    /// <summary>Toggles the run's pinned flag.</summary>
    public Task SetPinnedAsync(string runId, bool pinned, CancellationToken ct = default) =>
        MutateAsync(run => run.RunId == runId ? run with { Pinned = pinned } : run, ct);

    /// <summary>
    /// Applies <paramref name="transform"/> to every stored run and rewrites the JSONL files.
    /// Returning null from the transform drops that run; an emptied file is deleted.
    /// </summary>
    private async Task MutateAsync(Func<PersistedRun, PersistedRun?> transform, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
            return;

        await _writeLock.WaitAsync(ct);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "ui-*.jsonl"))
            {
                var survivors = new List<string>();
                foreach (var line in await File.ReadAllLinesAsync(file, ct))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    PersistedRun? run;
                    try
                    {
                        run = JsonSerializer.Deserialize<PersistedRun>(line, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        survivors.Add(line); // Preserve lines we can't parse rather than dropping them.
                        continue;
                    }

                    if (run is null)
                        continue;

                    if (transform(run) is { } kept)
                        survivors.Add(JsonSerializer.Serialize(kept, JsonOptions));
                }

                if (survivors.Count == 0)
                    File.Delete(file);
                else
                    await File.WriteAllLinesAsync(file, survivors, ct);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
