# Playwright Locators (v1.49+)

## Preferred locators — in order of preference

```js
page.getByRole('button', { name: 'Sign in' })
page.getByLabel('Password')
page.getByPlaceholder('name@example.com')
page.getByText('Welcome, John')
page.getByAltText('playwright logo')
page.getByTitle('Issues count')
page.getByTestId('submit-btn')  // uses data-testid by default
```

## Scoped and chained locators

```js
page.getByRole('listitem').filter({ hasText: 'Product 2' })
page.locator('.nav').getByRole('link')

const row = page.getByRole('row', { name: 'Alice' });
await row.getByRole('button', { name: 'Edit' }).click();
```

## Deprecated — do not generate

```js
// DEPRECATED — ElementHandle (stale, not auto-retrying)
const el = await page.$('.btn');    // avoid
const els = await page.$$('.btn');  // avoid
await el.click();

// DEPRECATED
await page.waitForSelector('.spinner');  // use locator.waitFor()
await page.type('#input', 'text');       // use locator.fill()

// RACE CONDITION PATTERN — avoid
await Promise.all([page.waitForNavigation(), page.click('a')]);
```

## Replacements for deprecated APIs

```js
// waitForSelector → locator.waitFor()
await page.getByRole('dialog').waitFor({ state: 'visible' });

// waitForNavigation → waitForURL
await page.getByRole('link', { name: 'Dashboard' }).click();
await page.waitForURL('**/dashboard');

// type → fill
await page.getByLabel('Email').fill('user@example.com');

// page.$ → getByRole / getByLabel / etc.
await page.getByRole('button', { name: 'Submit' }).click();
```

## Common AI mistakes

```js
// WRONG: Fragile CSS chain
page.locator('#app > div.container > ul > li:nth-child(2) > a')

// CORRECT: Semantic
page.getByRole('link', { name: 'Settings' })

// WRONG: force:true (hides real UI state)
await page.getByRole('button').click({ force: true });

// WRONG: networkidle as load signal (slow, unreliable)
await page.goto('/app', { waitUntil: 'networkidle' });
// CORRECT: wait for meaningful UI element
await page.goto('/app');
await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

// WRONG: arbitrary sleep
await page.waitForTimeout(5000);
// CORRECT: wait for condition
await expect(page.getByRole('status')).toHaveText('Saved');

// WRONG: pre-wait before auto-waiting action
await page.getByRole('button').waitFor({ state: 'visible' });
await page.getByRole('button').click(); // click already auto-waits — double wait
// CORRECT:
await page.getByRole('button').click();

// WRONG: response listener set up after action (may miss it)
await page.click('button');
await page.waitForResponse('/api/data');
// CORRECT: set up listener first
const responsePromise = page.waitForResponse('/api/data');
await page.click('button');
await responsePromise;
```

## Spec file shape

```typescript
import { test, expect } from '@playwright/test';

test.describe('<feature>', () => {
  test('happy path', async ({ page }) => {
    await page.goto('/path');
    await page.getByLabel('Email').fill('user@example.com');
    await page.getByRole('button', { name: 'Submit' }).click();
    await expect(page.getByText('Success')).toBeVisible();
    await expect(page).toHaveURL('/dashboard');
  });

  test('boundary — edge case', async ({ page }) => { /* ... */ });
  test('failure — error case', async ({ page }) => { /* ... */ });
});
```
