using FluentAssertions;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Domain.Tests;

public class SeniorityTests
{
    private static JobPosting Posting(string title) => new(title, "Co", "Remote", "https://x/1", "summary");

    private static JobPosting Posting(string title, string description) =>
        new(title, "Co", "Remote", "https://x/1", "summary", Description: description);

    [Theory]
    [InlineData("Senior Backend Engineer", SeniorityLevel.Senior)]
    [InlineData("Staff Software Engineer", SeniorityLevel.Lead)]
    [InlineData("Tech Lead", SeniorityLevel.Lead)]
    [InlineData("Engineering Manager", SeniorityLevel.Lead)]
    [InlineData("Principal Architect", SeniorityLevel.Principal)]
    [InlineData("Junior Frontend Developer", SeniorityLevel.Junior)]
    [InlineData("Backend Engineer (Go)", SeniorityLevel.Unknown)]
    [InlineData("Backend Software Engineer II", SeniorityLevel.Mid)]
    [InlineData("Software Engineer III", SeniorityLevel.Senior)]
    // The real Axon case: "Senior … II" carries both — Senior outranks the II level.
    [InlineData("Senior Backend Software Engineer II", SeniorityLevel.Senior)]
    public void Detect_reads_the_level_from_the_title(string title, SeniorityLevel expected) =>
        Seniority.Detect(title).Should().Be(expected);

    [Fact]
    public void Detect_takes_the_highest_level_word_present()
    {
        // "Senior Staff Engineer" carries both — Staff (Lead) outranks Senior.
        Seniority.Detect("Senior Staff Engineer").Should().Be(SeniorityLevel.Lead);
    }

    [Fact]
    public void Detect_matches_whole_words_only()
    {
        // "leadership" must not read as "lead".
        Seniority.Detect("Engineer with leadership skills").Should().Be(SeniorityLevel.Unknown);
    }

    [Fact]
    public void IsBelowFloor_flags_a_lower_level_posting()
    {
        Seniority.IsBelowFloor(Posting("Senior Backend Engineer"), "Lead").Should().BeTrue();
    }

    [Fact]
    public void IsBelowFloor_passes_an_equal_or_higher_level_posting()
    {
        Seniority.IsBelowFloor(Posting("Staff Engineer"), "Lead").Should().BeFalse();
        Seniority.IsBelowFloor(Posting("Principal Engineer"), "Lead").Should().BeFalse();
    }

    [Fact]
    public void IsBelowFloor_is_lenient_when_either_level_is_unknown()
    {
        // Posting title carries no level word → not penalised.
        Seniority.IsBelowFloor(Posting("Backend Engineer"), "Lead").Should().BeFalse();
        // No requested floor → nothing is below it.
        Seniority.IsBelowFloor(Posting("Senior Engineer"), "").Should().BeFalse();
    }

    [Fact]
    public void DetectFromPosting_falls_back_to_the_description_for_a_level_less_title()
    {
        // Title has no level word; the body names it → classify from the body.
        var posting = Posting(".NET Developer", "We're hiring a senior engineer to lead delivery.");
        Seniority.DetectFromPosting(posting).Should().Be(SeniorityLevel.Senior);
    }

    [Fact]
    public void DetectFromPosting_prefers_the_title_over_the_description()
    {
        // Title is authoritative: a Staff title wins even if the body mentions junior duties.
        var posting = Posting("Staff Engineer", "Mentor junior developers on the team.");
        Seniority.DetectFromPosting(posting).Should().Be(SeniorityLevel.Lead);
    }

    [Fact]
    public void IsBelowFloor_uses_the_description_to_exclude_a_level_less_title()
    {
        // A ".NET Developer" whose body reads as Senior is below a Lead floor → excluded.
        var posting = Posting(".NET Developer", "Senior backend role, 8+ years.");
        Seniority.IsBelowFloor(posting, "Lead").Should().BeTrue();
    }
}
