---
name: e2e
description: Run the JobAgents Playwright E2E suite end-to-end — clears port 5221, builds Release, launches the Blazor Server web app in the background, waits until it's ready, runs tests/JobAgents.E2E, and always stops the app. Use when asked to run E2E, Playwright, browser, or UI tests for this repo.
---

# Run the Playwright E2E suite

The E2E tests in `tests/JobAgents.E2E` drive the REAL running web app (Blazor Server) with Playwright — they never auto-start a server. So the flow is: launch the app, wait for it, run the tests against it, then stop it. No API keys are needed (the suite only exercises navigation/form/settings UI, never the LLM agents).

Target: `http://localhost:5221` (override with the `E2E_BASE_URL` env var).

## Steps

Run on Windows / PowerShell (this is the project's primary dev OS). Each step is a separate tool call so failures surface clearly.

1. **Free port 5221** — orphaned `dotnet run` instances hold it and block a new bind (`pkill` does not exist on Windows):
   ```powershell
   try { Get-NetTCPConnection -LocalPort 5221 -State Listen -EA Stop | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -EA SilentlyContinue } } catch {}
   ```

2. **Build** Release (skip if a build is already current and nothing changed):
   ```powershell
   dotnet build tests/JobAgents.E2E/JobAgents.E2E.csproj -c Release
   ```

3. **Ensure browsers** (first run only — Chromium):
   ```powershell
   pwsh tests/JobAgents.E2E/bin/Release/net9.0/playwright.ps1 install chromium
   ```

4. **Launch the app in the background** (`run_in_background: true`). Use `--no-launch-profile` so it binds the URL below, not a profile port:
   ```powershell
   $env:ASPNETCORE_URLS="http://localhost:5221"; dotnet run --project src/JobAgents.Web -c Release --no-build --no-launch-profile
   ```

5. **Wait until ready** (poll, don't sleep blindly):
   ```powershell
   $ok=$false; for($i=0;$i -lt 60;$i++){ try{ $r=Invoke-WebRequest http://localhost:5221 -UseBasicParsing -TimeoutSec 3; if($r.StatusCode -eq 200){$ok=$true;break} }catch{ Start-Sleep -Milliseconds 800 } }; "ready=$ok"
   ```
   If `ready=False`, read the background app's output file before giving up.

6. **Run the tests** against the live app:
   ```powershell
   $env:E2E_BASE_URL="http://localhost:5221"; dotnet test tests/JobAgents.E2E/JobAgents.E2E.csproj -c Release --no-build
   ```

7. **Always stop the app** afterward, pass or fail (repeat step 1's port-clear). Leaving it running holds the port for the next run.

## Bash / WSL / CI variant

Same flow on Linux/macOS (or when reproducing the CI `e2e` job). `Get-NetTCPConnection` doesn't exist here — free the port with `fuser`/`lsof`, and background with `&`.

1. **Free port 5221:**
   ```bash
   fuser -k 5221/tcp 2>/dev/null || true
   ```

2. **Build + browsers (first run):**
   ```bash
   dotnet build tests/JobAgents.E2E/JobAgents.E2E.csproj -c Release
   pwsh tests/JobAgents.E2E/bin/Release/net9.0/playwright.ps1 install --with-deps chromium
   ```

3. **Launch app in background, capture PID:**
   ```bash
   ASPNETCORE_URLS=http://localhost:5221 ASPNETCORE_ENVIRONMENT=Development \
     dotnet run --project src/JobAgents.Web -c Release --no-build --no-launch-profile > web.log 2>&1 &
   echo "WEB_PID=$!"
   ```

4. **Wait until ready:**
   ```bash
   for i in $(seq 1 60); do curl -fsS http://localhost:5221 >/dev/null 2>&1 && { echo "app is up"; break; }; sleep 1; done
   ```

5. **Run tests:**
   ```bash
   E2E_BASE_URL=http://localhost:5221 dotnet test tests/JobAgents.E2E/JobAgents.E2E.csproj -c Release --no-build
   ```

6. **Stop app (always):**
   ```bash
   kill "$WEB_PID" 2>/dev/null || true
   ```

## Notes / gotchas

- **Don't E2E `Find jobs` / `Analyze`** — those need OpenAI + Tavily keys, are slow, and cost money. The suite deliberately avoids them.
- **Blazor prerender race:** the page serves static HTML first, then the SignalR circuit connects and wires up `@bind`/`@onclick`. Interactions before the circuit is live are dropped. Tests handle this with retry-until-effect helpers; if you add a test, do the same (fill+blur, then poll for the observable effect).
- **`@bind` commits on blur:** Playwright `FillAsync` only fires `input`; call `BlurAsync()` after so Blazor's `change`-event bind commits.
- The suite is serialized (`DisableTestParallelization`) because all tests share one app + on-disk state (`results/profile.json`, model-config store).
- CI runs this in a dedicated `e2e` job (boots the app with `ASPNETCORE_ENVIRONMENT=Development`, curl-waits, then `dotnet test ... --no-build`). Mirror CI when debugging CI-only failures.
