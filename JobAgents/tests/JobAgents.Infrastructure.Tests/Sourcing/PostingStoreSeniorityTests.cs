using FluentAssertions;
using JobAgents.Domain.JobHunt;
using JobAgents.Infrastructure.Sourcing;

namespace JobAgents.Infrastructure.Tests.Sourcing;

/// <summary>
/// The posting cache must not serve back roles below the requested seniority — even though role-token
/// matching itself stays level-agnostic (it strips "senior/lead/staff"). Verifies the seniority gate.
/// </summary>
public sealed class PostingStoreSeniorityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jobagents-test-" + Guid.NewGuid().ToString("N"));

    private static JobPosting Posting(string title) =>
        new(title, "Acme", "Remote", $"https://x/{title.GetHashCode()}", "Backend engineer role", Description: "C# .NET backend");

    private static SearchCriteria Criteria(string seniority) => new(
        Roles: ["Backend Engineer"], Locations: ["Remote"], Seniority: seniority,
        MustHaveSkills: [], NiceToHaveSkills: [], WorkStyles: ["Remote"], SalaryExpectation: null);

    [Fact]
    public async Task Query_excludes_postings_below_the_requested_seniority()
    {
        var store = new FilePostingStore(_dir);
        await store.SaveAsync(new[]
        {
            Posting("Senior Backend Engineer"),
            Posting("Staff Backend Engineer"),
            Posting("Principal Backend Engineer"),
        });

        var result = store.Query(Criteria("Lead"), postedWithin: null, max: 10);

        // Senior is below the Lead floor; Staff (Lead) and Principal are at/above it.
        result.Select(p => p.Title).Should().BeEquivalentTo("Staff Backend Engineer", "Principal Backend Engineer");
    }

    [Fact]
    public async Task Query_keeps_level_less_postings_when_a_floor_is_set()
    {
        var store = new FilePostingStore(_dir);
        await store.SaveAsync(new[] { Posting("Backend Engineer") }); // no level word in title

        // Unknown posting level is treated leniently — still served.
        store.Query(Criteria("Lead"), postedWithin: null, max: 10).Should().ContainSingle();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
