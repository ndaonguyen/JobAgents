using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace JobAgents.E2E;

// Shared base for the E2E suite.
//
// Blazor Server prerenders static HTML, then connects a SignalR circuit that wires up
// @bind / @onclick. Anything that drives an interactive handler (click, change-commit)
// is silently dropped against the pre-circuit HTML, so a single attempt is racy on slow
// CI. RetryUntilAsync re-applies an action until its effect is observable — the signal
// that the circuit is live.
public abstract class E2ETestBase : PageTest
{
    protected static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5221";

    // Re-run `act` until `verify` passes (or attempts run out). `verify` should use a short
    // Expect timeout so each round fails fast and we re-apply the action.
    protected async Task RetryUntilAsync(Func<Task> act, Func<Task> verify, int maxAttempts = 10)
    {
        for (var attempt = 1; ; attempt++)
        {
            await act();
            try
            {
                await verify();
                return;
            }
            catch (PlaywrightException) when (attempt < maxAttempts)
            {
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }
}
