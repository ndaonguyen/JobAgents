using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JobAgents.E2E;

// True end-to-end of the Save CV → reload → Forget CV lifecycle. The CV round-trips
// through ProfileStore on disk; a reloaded page must pre-fill the textarea from the
// saved profile. No API keys needed — we never click "Find jobs".
//
// NOTE: this overwrites then clears the saved profile on whatever machine runs it.
public class CvProfileTests : E2ETestBase
{
    private const string Cv = "Senior C# engineer, 8 years .NET, AWS, Kafka, distributed systems.";

    private ILocator ResumeBox => Page.GetByPlaceholder("Paste your resume text here...");
    private ILocator SaveCvButton => Page.GetByRole(AriaRole.Button, new() { Name = "Save CV" });
    private ILocator ForgetCvButton => Page.GetByRole(AriaRole.Button, new() { Name = "Forget saved CV" });

    [Fact]
    public async Task Save_then_reload_prefills_cv_then_forget_clears_it()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(ResumeBox).ToBeVisibleAsync();

        try
        {
            // Type the CV. @bind commits on blur ("change"), and Save CV only enables once
            // _resume is non-empty — retry until the circuit is live and the button flips on.
            await RetryUntilAsync(
                act: async () =>
                {
                    await ResumeBox.FillAsync(Cv);
                    await ResumeBox.BlurAsync();
                },
                verify: () => Expect(SaveCvButton).ToBeEnabledAsync(new() { Timeout = 2000 }));

            await SaveCvButton.ClickAsync();
            await Expect(Page.GetByText("CV saved")).ToBeVisibleAsync();

            // Reload: OnInitializedAsync must hydrate the textarea from the saved profile,
            // and the "Forget saved CV" affordance must now exist.
            await Page.GotoAsync(BaseUrl);
            await Expect(ResumeBox).ToHaveValueAsync(Cv);
            await Expect(ForgetCvButton).ToBeVisibleAsync();
            await Expect(Page.GetByText("Loaded your saved CV")).ToBeVisibleAsync();
        }
        finally
        {
            // Always clear the saved profile so the suite is repeatable and the dev box is clean.
            await RetryUntilAsync(
                act: () => ForgetCvButton.ClickAsync(),
                verify: () => Expect(Page.GetByText("Saved CV removed")).ToBeVisibleAsync(new() { Timeout = 2000 }));
            await Expect(ForgetCvButton).ToBeHiddenAsync();
        }
    }
}
