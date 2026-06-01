using System.Text.Json;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Web.Services;

/// <summary>A persisted job-hunt run, appended to a daily JSONL file and listed on /past-runs.</summary>
public sealed record PersistedRun(
    string RunId,
    DateTime CompletedAtUtc,
    string Preferences,
    decimal? EstimatedCostUsd,
    JobHuntResult Result);

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

        return runs.OrderByDescending(r => r.CompletedAtUtc).ToList();
    }
}
