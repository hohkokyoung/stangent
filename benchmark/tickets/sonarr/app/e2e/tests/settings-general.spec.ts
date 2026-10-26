// Seeded for the Phase 3 benchmark (D86): Sonarr has no Playwright tests of its own. Written on a screen chosen without
// looking at the benchmark's tickets.
import { test, expect } from '@playwright/test';

test('general settings show the port and offer to save an edit', async ({ page }) => {
  await page.goto('/settings/general');
  const main = page.getByRole('main', { name: 'General Settings' });
  await expect(main.getByRole('heading', { name: 'Settings · General', level: 1 })).toBeVisible();
  await expect(main.getByRole('spinbutton', { name: 'Port Number' })).toHaveValue('8989');
  await expect(main.getByRole('button', { name: 'No Changes' })).toBeDisabled();

  await main.getByRole('textbox', { name: 'URL Base' }).fill('/sonarr');
  await expect(main.getByRole('button', { name: 'Save Changes' })).toBeEnabled();
});
