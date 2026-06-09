using System.Text;
using JobAgents.Domain.JobHunt;
using JobAgents.Infrastructure.Feedback;

namespace JobAgents.Evals;

/// <summary>
/// Turns human-scored real matches (captured in the web app's <c>feedback-*.jsonl</c>) into eval
/// <see cref="MatchCase"/>s, so the matcher can be calibrated against real judgements rather than only
/// the synthetic golden set. The human score becomes the case's TargetScore, with a tolerance band
/// around it; the matcher must land inside that band on a majority of trials to pass.
/// </summary>
internal static class FeedbackCases
{
    /// <summary>How far (± points) the matcher's score may sit from the human score and still pass.</summary>
    private const int DefaultBand = 12;

    public static async Task<IReadOnlyList<MatchCase>> LoadAsync(string feedbackDir, int band = DefaultBand)
    {
        var store = new FeedbackStore(feedbackDir);
        var feedback = await store.LoadAllAsync();

        // De-duplicate by (resume, posting) keeping the latest score, so re-scoring a match supersedes.
        var latest = feedback
            .GroupBy(f => $"{Hash(f.Resume)}|{Key(f.Posting)}")
            .Select(g => g.OrderBy(f => f.CreatedAtUtc).Last())
            .ToList();

        return latest.Select((f, i) => new MatchCase(
            Name: BuildName(f, i),
            Resume: f.Resume,
            Posting: f.Posting,
            Criteria: f.Criteria,
            MinScore: Math.Max(0, f.HumanScore - band),
            MaxScore: Math.Min(100, f.HumanScore + band),
            // We don't ask the human to label expected skills, so leave this empty (the eval then skips
            // the skills check and relies on the grounding judge instead). The score band is the signal.
            ExpectedMatchedSkills: [],
            TargetScore: f.HumanScore)).ToList();
    }

    private static string BuildName(MatchFeedback f, int index)
    {
        var raw = $"fb-{f.Posting.Company}-{f.Posting.Title}";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 48)
            slug = slug[..48].Trim('-');
        return $"{slug}-{index}";
    }

    private static string Key(JobPosting p) =>
        !string.IsNullOrWhiteSpace(p.Url)
            ? p.Url.Trim().ToLowerInvariant()
            : $"{p.Title}|{p.Company}".Trim().ToLowerInvariant();

    private static int Hash(string value) => value.GetHashCode(StringComparison.Ordinal);
}
