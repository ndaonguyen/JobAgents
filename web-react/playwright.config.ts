import { defineConfig, devices } from '@playwright/test';

// Smoke E2E for the React stack. Playwright boots BOTH servers it needs:
//   • JobAgents.Api on :5300 (REST + SSE backend)
//   • the Vite dev server on :5173 (proxies /api → :5300)
// then drives the React app in Chromium. No API keys required — the smoke spec only exercises
// navigation + the API-backed Settings catalog, never the LLM agents.
const CI = !!process.env.CI;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: CI,
  retries: CI ? 2 : 0,
  workers: 1,
  reporter: CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../src/JobAgents.Api -c Release',
      url: 'http://localhost:5300/',
      reuseExistingServer: !CI,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: !CI,
      timeout: 120_000,
    },
  ],
});
