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
        await Expect(ResumeBox).ToBeVisibleAsync();

        // Blazor Server interactivity arrives asynchronously: the page prerenders as
        // static HTML, then the SignalR circuit connects and wires up @bind. Until then,
        // Fill+Blur run against dead HTML and the bound value (committed on the "change"
        // event, i.e. blur) is silently dropped — a single attempt is racy on slow CI.
        // Retry the fill+blur until the circuit is live and the button flips to enabled.
        await FillResumeUntilEnabledAsync("Senior C# engineer, 6 years .NET, AWS, Kafka.");
    }

    private async Task FillResumeUntilEnabledAsync(string text)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            await ResumeBox.FillAsync(text);   // sets value, fires "input"
            await ResumeBox.BlurAsync();       // fires "change" so Blazor @bind commits

            try
            {
                await Expect(FindJobsButton).ToBeEnabledAsync(new() { Timeout = 2000 });
                return;
            }
            catch (PlaywrightException) when (attempt < maxAttempts)
            {
                // Circuit not interactive yet — wait briefly, then re-type.
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }
}
