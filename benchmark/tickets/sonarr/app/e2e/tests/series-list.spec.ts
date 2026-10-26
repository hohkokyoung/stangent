// Seeded for the Phase 3 benchmark (D86): Sonarr has no Playwright tests of its own. Written on a screen chosen without
// looking at the benchmark's tickets; it uses the two series stangent's start step seeds.
import { test, expect } from '@playwright/test';

test('the series list shows the seeded series and opens one', async ({ page }) => {
  await page.goto('/series');
  const main = page.getByRole('main', { name: 'Series' });
  await expect(main.getByRole('link', { name: 'The Test Show' })).toBeVisible();
  await expect(main.getByRole('link', { name: 'Second Show' })).toBeVisible();

  await main.getByRole('link', { name: 'The Test Show' }).click();
  await expect(page).toHaveURL(/\/series\/[^/]+$/);
});
