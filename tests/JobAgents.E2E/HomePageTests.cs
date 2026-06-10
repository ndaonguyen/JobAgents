using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace JobAgents.E2E;

// Playwright E2E against the running JobAgents.Web app.
//
// PREREQ: the web app must already be running. In a separate terminal:
//   dotnet run --project src/JobAgents.Web
// then run these tests:
//   dotnet test tests/JobAgents.E2E
//
// Base URL defaults to the http launch profile (localhost:5221); override with
// the E2E_BASE_URL environment variable.
//
// PageTest gives each test a fresh browser Page for free. Inherit it and use
// Page + Expect(...) directly.
public class HomePageTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5221";

    [Fact]
    public async Task Home_has_correct_title()
    {
        await Page.GotoAsync(BaseUrl);

        // Web-first assertion: auto-waits and retries until the title matches.
        await Expect(Page).ToHaveTitleAsync("JobAgents");
    }

    [Fact]
    public async Task Home_shows_job_hunt_heading()
    {
        await Page.GotoAsync(BaseUrl);

        // GetByRole is the preferred locator — matches how users/AT see the page.
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "Job Hunt" });
        await Expect(heading).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Nav_to_settings_works()
    {
        await Page.GotoAsync(BaseUrl);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();

        // After Blazor routes, the URL ends in /settings.
        await Expect(Page).ToHaveURLAsync(new Regex(".*/settings"));
    }
}
