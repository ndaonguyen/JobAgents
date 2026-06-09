using System.Text;

namespace JobAgents.Domain.JobHunt;

/// <summary>The seniority ladder, ordered so a higher level compares greater than a lower one.</summary>
public enum SeniorityLevel
{
    Unknown = 0,
    Junior = 1,
    Mid = 2,
    Senior = 3,
    Lead = 4,       // Lead / Staff / Manager / Head / Director
    Principal = 5,  // Principal / Distinguished / Fellow
}

/// <summary>
/// Detects a posting's seniority from its title and compares it to the level the candidate asked for.
/// Used to keep below-target roles out of the results: the posting cache excludes them, and the matcher
/// caps their score (a soft down-rank, not a hard delete). Detection is title-based and word-exact, so
/// "leadership" never reads as "Lead"; when a title carries no level word the result is
/// <see cref="SeniorityLevel.Unknown"/> and callers treat it leniently (never filtered/penalised).
/// </summary>
public static class Seniority
{
    // Whole-word → level. Highest matching word in a title wins (e.g. "Senior Staff Engineer" → Lead).
    private static readonly Dictionary<string, SeniorityLevel> WordLevels = new(StringComparer.Ordinal)
    {
        ["intern"] = SeniorityLevel.Junior,
        ["internship"] = SeniorityLevel.Junior,
        ["trainee"] = SeniorityLevel.Junior,
        ["graduate"] = SeniorityLevel.Junior,
        ["junior"] = SeniorityLevel.Junior,
        ["jr"] = SeniorityLevel.Junior,
        ["entry"] = SeniorityLevel.Junior,
        ["mid"] = SeniorityLevel.Mid,
        ["intermediate"] = SeniorityLevel.Mid,
        // Numeric leveling: "Engineer I" entry/junior, "II" mid, "III" senior-equivalent. ("I" is too
        // noisy a token to map, so only II/III are recognised.)
        ["ii"] = SeniorityLevel.Mid,
        ["iii"] = SeniorityLevel.Senior,
        ["senior"] = SeniorityLevel.Senior,
        ["sr"] = SeniorityLevel.Senior,
        ["lead"] = SeniorityLevel.Lead,
        ["staff"] = SeniorityLevel.Lead,
        ["manager"] = SeniorityLevel.Lead,
        ["head"] = SeniorityLevel.Lead,
        ["director"] = SeniorityLevel.Lead,
        ["vp"] = SeniorityLevel.Lead,
        ["principal"] = SeniorityLevel.Principal,
        ["distinguished"] = SeniorityLevel.Principal,
        ["fellow"] = SeniorityLevel.Principal,
    };

    /// <summary>The highest level word found in <paramref name="text"/>, or Unknown if none.</summary>
    public static SeniorityLevel Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SeniorityLevel.Unknown;

        var best = SeniorityLevel.Unknown;
        foreach (var word in Words(text))
            if (WordLevels.TryGetValue(word, out var level) && level > best)
                best = level;

        return best;
    }

    /// <summary>
    /// Detects a posting's level from its title (the reliable level-bearing field). When the title
    /// carries no level word, falls back to the posting body — so level-less titles like
    /// ".NET Developer" whose description names the level (e.g. "senior role") still get classified.
    /// </summary>
    public static SeniorityLevel DetectFromPosting(JobPosting posting)
    {
        var fromTitle = Detect(posting.Title);
        return fromTitle != SeniorityLevel.Unknown ? fromTitle : Detect(posting.Description);
    }

    /// <summary>Parses the requested seniority string (e.g. "Lead", "Senior") into a level.</summary>
    public static SeniorityLevel Parse(string? requested) => Detect(requested);

    /// <summary>
    /// True when the posting's detected level is clearly below the requested floor. Unknown on either
    /// side returns false (lenient): we only exclude/penalise when we're confident the level is too low.
    /// </summary>
    public static bool IsBelowFloor(JobPosting posting, string? requested) =>
        IsBelowFloor(posting, Parse(requested));

    /// <summary>As above, given an already-parsed floor.</summary>
    public static bool IsBelowFloor(JobPosting posting, SeniorityLevel floor)
    {
        if (floor == SeniorityLevel.Unknown)
            return false;

        var level = DetectFromPosting(posting);
        return level != SeniorityLevel.Unknown && level < floor;
    }

    /// <summary>Splits text into lower-cased alphanumeric words (so "leadership" ≠ "lead").</summary>
    private static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }
}
