using System.Text.Json;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Infrastructure.Feedback;

/// <summary>
/// Appends human-scored matches to <c>feedback-{yyyyMMdd}.jsonl</c> (one JSON object per line) in the
/// results directory, and reads them back so the eval can turn them into calibration cases. Same
/// simple file storage as <c>RunStore</c> / <c>FilePostingStore</c> — no database needed.
/// </summary>
public sealed class FeedbackStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task SaveAsync(MatchFeedback feedback, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"feedback-{feedback.CreatedAtUtc:yyyyMMdd}.jsonl");
        var line = JsonSerializer.Serialize(feedback, JsonOptions);

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

    public async Task<IReadOnlyList<MatchFeedback>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<MatchFeedback>();

        var items = new List<MatchFeedback>();
        foreach (var file in Directory.EnumerateFiles(directory, "feedback-*.jsonl"))
        {
            foreach (var line in await File.ReadAllLinesAsync(file, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    if (JsonSerializer.Deserialize<MatchFeedback>(line, JsonOptions) is { } item)
                        items.Add(item);
                }
                catch (JsonException)
                {
                    // Skip malformed lines rather than failing the whole read.
                }
            }
        }

        return items.OrderBy(f => f.CreatedAtUtc).ToList();
    }
}
