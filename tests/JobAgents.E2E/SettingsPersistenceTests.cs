using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JobAgents.E2E;

// True end-to-end: drive the Settings UI, let it persist to disk via ModelConfigStore,
// reload the page, and assert the value survived the round-trip. No API keys needed —
// this never starts a hunt.
public class SettingsPersistenceTests : E2ETestBase
{
    private string SettingsUrl => $"{BaseUrl}/settings";

    // The "Saved ✓" pill only renders after an interactive @onchange handler runs, so it
    // doubles as proof the circuit is live and the write actually happened. Scope to the
    // pill's class so it doesn't collide with the page's "Saved automatically…" subtitle.
    private ILocator SavePill => Page.Locator(".save-pill");

    private ILocator ResumeLimit => Page.Locator("#lim-resume");
    private ILocator ParallelSwitch => Page.Locator("#parallel-search");

    [Fact]
    public async Task Input_limit_persists_across_reload()
    {
        await Page.GotoAsync(SettingsUrl);
        await Expect(ResumeLimit).ToBeVisibleAsync();

        // Setting a number is idempotent (no toggle-parity races), so it's a safe way to
        // both prove the circuit is live and exercise the save path.
        await RetryUntilAsync(
            act: async () =>
            {
                await ResumeLimit.FillAsync("12345");
                await ResumeLimit.BlurAsync(); // fires "change" → @onchange → SaveAsync
            },
            verify: () => Expect(SavePill).ToBeVisibleAsync(new() { Timeout = 2000 }));

        // Reload from disk: a fresh OnInitializedAsync must read back what we wrote.
        await Page.GotoAsync(SettingsUrl);
        await Expect(ResumeLimit).ToHaveValueAsync("12345");

        // Restore the default so we don't leave the dev profile mutated.
        await RetryUntilAsync(
            act: async () =>
            {
                await ResumeLimit.FillAsync("0");
                await ResumeLimit.BlurAsync();
            },
            verify: () => Expect(SavePill).ToBeVisibleAsync(new() { Timeout = 2000 }));
    }

    [Fact]
    public async Task Parallel_search_toggle_persists_across_reload()
    {
        await Page.GotoAsync(SettingsUrl);
        await Expect(ParallelSwitch).ToBeVisibleAsync();

        // Gate on circuit liveness with an idempotent write first, so the toggle click below
        // is guaranteed to hit a live handler (avoids double-toggle parity bugs from dead clicks).
        await RetryUntilAsync(
            act: async () =>
            {
                await ResumeLimit.FillAsync("0");
                await ResumeLimit.BlurAsync();
            },
            verify: () => Expect(SavePill).ToBeVisibleAsync(new() { Timeout = 2000 }));

        var original = await ParallelSwitch.IsCheckedAsync();
        var target = !original;

        await ParallelSwitch.SetCheckedAsync(target);
        await Expect(SavePill).ToBeVisibleAsync();
        await Expect(ParallelSwitch).ToBeCheckedAsync(new() { Checked = target });

        // Reload: the flipped state must come back from disk.
        await Page.GotoAsync(SettingsUrl);
        await Expect(ParallelSwitch).ToBeCheckedAsync(new() { Checked = target });

        // Restore the original value.
        await ParallelSwitch.SetCheckedAsync(original);
        await Expect(SavePill).ToBeVisibleAsync();
    }
}
