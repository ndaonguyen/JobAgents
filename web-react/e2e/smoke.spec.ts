import { expect, test } from '@playwright/test';

// Smoke coverage for the React front end + its API wiring. Mirrors the no-key Blazor E2E set:
// navigation across every route, plus one assertion that proves the SPA reaches JobAgents.Api
// through the Vite proxy (Settings populates its model dropdowns from /api/settings/catalog).

test('home renders the job hunt form', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Job Hunt', level: 1 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Find jobs' })).toBeVisible();
});

test('navigates across every route', async ({ page }) => {
  await page.goto('/');
  for (const [link, heading] of [
    ['JD Analyzer', 'JD Analyzer'],
    ['Past Runs', 'Past Runs'],
    ['Roadmap', 'Roadmap'],
    ['Settings', 'Settings'],
    ['Job Hunt', 'Job Hunt'],
  ] as const) {
    await page.getByRole('link', { name: link }).click();
    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible();
  }
});

test('settings loads the model catalog from the API', async ({ page }) => {
  await page.goto('/settings');
  await expect(page.getByRole('heading', { name: 'Settings', level: 1 })).toBeVisible();

  // The "Coordinator" model dropdown is populated by GET /api/settings/catalog (proxied to the API).
  // A non-empty option list proves the React → Vite proxy → JobAgents.Api path works end to end.
  const firstSelect = page.locator('select').first();
  await expect(firstSelect).toBeVisible();
  await expect(firstSelect.locator('option')).not.toHaveCount(0);
  await expect(firstSelect.getByRole('option', { name: 'Claude Haiku 4.5' })).toHaveCount(1);
});
