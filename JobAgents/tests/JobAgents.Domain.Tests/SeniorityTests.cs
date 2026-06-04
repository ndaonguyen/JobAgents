using FluentAssertions;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Domain.Tests;

public class SeniorityTests
{
    private static JobPosting Posting(string title) => new(title, "Co", "Remote", "https://x/1", "summary");

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
}
