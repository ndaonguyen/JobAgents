using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JobAgents.E2E;

// Walks the whole nav menu, asserting each route loads its page. Anchor navigation works
// without the circuit, so no retry dance is needed here.
public class NavigationTests : E2ETestBase
{
    public static TheoryData<string, string, string> NavTargets => new()
    {
        // link text, URL suffix, page heading
        { "JD Analyzer", "/jd-analyzer", "JD Analyzer" },
        { "Past Runs", "/past-runs", "Past Runs" },
        { "Roadmap", "/roadmap", "Roadmap" },
        { "Settings", "/settings", "Settings" },
    };

    [Theory]
    [MemberData(nameof(NavTargets))]
    public async Task Nav_link_loads_page(string linkText, string urlSuffix, string heading)
    {
        await Page.GotoAsync(BaseUrl);

        await Page.GetByRole(AriaRole.Link, new() { Name = linkText }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(urlSuffix)}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = heading })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Brand_link_returns_home()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");

        await Page.GetByRole(AriaRole.Link, new() { Name = "JobAgents", Exact = true }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Job Hunt" })).ToBeVisibleAsync();
    }
}
