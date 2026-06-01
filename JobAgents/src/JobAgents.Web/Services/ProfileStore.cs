using System.Text.Json;

namespace JobAgents.Web.Services;

/// <summary>The candidate's saved resume, reused across sessions so they don't re-paste each visit.</summary>
public sealed record CandidateProfile(string ResumeText, DateTime UpdatedAtUtc);

/// <summary>
/// Stores a single reusable CV as <c>results/profile.json</c>. This is plain-text PII on disk, so the
/// <c>results/</c> directory is git-ignored. Best-effort, single-profile storage for v1.
/// </summary>
public sealed class ProfileStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _file = Path.Combine(directory, "profile.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool Exists => File.Exists(_file);

    public async Task SaveAsync(string resumeText, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var profile = new CandidateProfile(resumeText, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        await _lock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(_file, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CandidateProfile?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_file))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_file, ct);
            return JsonSerializer.Deserialize<CandidateProfile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteAsync()
    {
        if (File.Exists(_file))
            File.Delete(_file);
        return Task.CompletedTask;
    }
}
