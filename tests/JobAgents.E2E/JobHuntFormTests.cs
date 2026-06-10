using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace JobAgents.E2E;

// Worked example: locating form controls and asserting on their state.
// Still no API keys needed — we never click "Find jobs", only check the form reacts.
public class JobHuntFormTests : PageTest
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5221";

    // The submit button. GetByRole matches the <button type="submit"> by its visible text.
    private ILocator FindJobsButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Find jobs" });

    // The resume textarea, located by its placeholder text.
    private ILocator ResumeBox =>
        Page.GetByPlaceholder("Paste your resume text here...");

    [Fact]
    public async Task FindJobs_is_disabled_when_resume_empty()
    {
        await Page.GotoAsync(BaseUrl);

        // Page starts with an empty resume, so the button must be disabled.
        await Expect(FindJobsButton).ToBeDisabledAsync();
    }

    [Fact]
    public async Task FindJobs_enables_after_typing_resume()
    {
        await Page.GotoAsync(BaseUrl);

        // Act: type into the textarea. FillAsync clears + sets the value.
        await ResumeBox.FillAsync("Senior C# engineer, 6 years .NET, AWS, Kafka.");

        // Blazor's @bind commits on the DOM "change" event, which only fires on
        // blur — FillAsync alone fires "input". Blur to commit so _resume updates.
        await ResumeBox.BlurAsync();

        // State round-trips over SignalR, so the button flips to enabled.
        // Expect(...) auto-waits for that to happen.
        await Expect(FindJobsButton).ToBeEnabledAsync();
    }
}
