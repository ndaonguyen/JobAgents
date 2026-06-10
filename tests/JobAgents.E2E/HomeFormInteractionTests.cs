using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JobAgents.E2E;

// Interactive behaviour of the Home form that needs a live circuit but no API keys:
// the resume input-mode switch and the bulk role select/clear links.
public class HomeFormInteractionTests : E2ETestBase
{
    private ILocator PasteTab => Page.GetByRole(AriaRole.Button, new() { Name = "Paste text" });
    private ILocator UploadTab => Page.GetByRole(AriaRole.Button, new() { Name = "Upload file" });
    private ILocator ResumeBox => Page.GetByPlaceholder("Paste your resume text here...");
    private ILocator FileInput => Page.Locator("input[type=file]");

    // One known role checkbox, located by its exact id (the id contains a space).
    private ILocator BackendRole => Page.Locator("input[id='role-Backend Engineer']");

    [Fact]
    public async Task Resume_mode_switch_swaps_textarea_and_file_input()
    {
        await Page.GotoAsync(BaseUrl);

        // Defaults to Paste mode: the textarea is shown, no file picker.
        await Expect(ResumeBox).ToBeVisibleAsync();

        // Switch to Upload: textarea disappears, file picker appears. Retry for circuit liveness.
        await RetryUntilAsync(
            act: () => UploadTab.ClickAsync(),
            verify: () => Expect(FileInput).ToBeVisibleAsync(new() { Timeout = 2000 }));
        await Expect(ResumeBox).ToBeHiddenAsync();

        // Switch back to Paste: textarea returns.
        await RetryUntilAsync(
            act: () => PasteTab.ClickAsync(),
            verify: () => Expect(ResumeBox).ToBeVisibleAsync(new() { Timeout = 2000 }));
    }

    [Fact]
    public async Task Roles_all_then_clear_toggles_checkboxes()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(BackendRole).ToBeVisibleAsync();

        // The first "all" / "clear" pair on the page belongs to the Roles column.
        var allLink = Page.GetByRole(AriaRole.Button, new() { Name = "all" }).First;
        var clearLink = Page.GetByRole(AriaRole.Button, new() { Name = "clear" }).First;

        await RetryUntilAsync(
            act: () => allLink.ClickAsync(),
            verify: () => Expect(BackendRole).ToBeCheckedAsync(new() { Timeout = 2000 }));

        await RetryUntilAsync(
            act: () => clearLink.ClickAsync(),
            verify: () => Expect(BackendRole).Not.ToBeCheckedAsync(new() { Timeout = 2000 }));
    }
}
