using FluentAssertions;
using JobAgents.Domain.JobHunt;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Infrastructure.Tests.Agents;

/// <summary>
/// Output-quality scoring for the (deterministic) dedupe step. Unlike the LLM evals, this is exact
/// and free: a labelled list with known duplicates must collapse to a known expected set.
/// </summary>
public sealed class DedupeTests
{
    private static JobPosting Posting(string title, string company, string url) =>
        new(title, company, "Remote", url, "summary");

    [Fact]
    public void Collapses_postings_with_the_same_url()
    {
        var input = new[]
        {
            Posting("Backend Engineer", "Acme", "https://jobs.acme.com/1"),
            Posting("Backend Engineer (reposted)", "Acme", "https://jobs.acme.com/1"),
        };

        var result = Coordinator.Dedupe(input);

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Backend Engineer"); // keeps the first occurrence
    }

    [Fact]
    public void Treats_url_case_insensitively()
    {
        var input = new[]
        {
            Posting("Dev", "Acme", "https://Jobs.Acme.com/ABC"),
            Posting("Dev", "Acme", "https://jobs.acme.com/abc"),
        };

        Coordinator.Dedupe(input).Should().ContainSingle();
    }

    [Fact]
    public void Falls_back_to_title_and_company_when_url_is_missing()
    {
        var input = new[]
        {
            Posting("Backend Engineer", "Acme", ""),
            Posting("Backend Engineer", "Acme", ""),   // same title+company, no url -> duplicate
            Posting("Backend Engineer", "Globex", ""), // different company -> distinct
        };

        var result = Coordinator.Dedupe(input);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Keeps_distinct_urls_even_with_same_title_and_company()
    {
        var input = new[]
        {
            Posting("Backend Engineer", "Acme", "https://jobs.acme.com/1"),
            Posting("Backend Engineer", "Acme", "https://jobs.acme.com/2"),
        };

        Coordinator.Dedupe(input).Should().HaveCount(2);
    }

    [Fact]
    public void Keeps_all_distinct_postings_and_preserves_order()
    {
        var input = new[]
        {
            Posting("A", "Co1", "https://x/a"),
            Posting("B", "Co2", "https://x/b"),
            Posting("C", "Co3", "https://x/c"),
        };

        var result = Coordinator.Dedupe(input);

        result.Should().HaveCount(3);
        result.Select(p => p.Title).Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public void Empty_input_yields_empty_output()
    {
        Coordinator.Dedupe(Array.Empty<JobPosting>()).Should().BeEmpty();
    }
}
