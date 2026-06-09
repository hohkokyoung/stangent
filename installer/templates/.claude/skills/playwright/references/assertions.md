# Playwright Assertions (v1.49+)

All `expect(locator)` assertions auto-retry until timeout (default 5s). Use these over manual checks.

## Visibility

```js
await expect(locator).toBeVisible();
await expect(locator).toBeHidden();       // prefer over .not.toBeVisible()
await expect(locator).toBeAttached();     // in DOM (may be invisible)
await expect(locator).toBeInViewport();
```

## State

```js
await expect(locator).toBeEnabled();
await expect(locator).toBeDisabled();
await expect(locator).toBeChecked();
await expect(locator).toBeChecked({ checked: false });
await expect(locator).toBeEditable();
await expect(locator).toBeEmpty();
await expect(locator).toBeFocused();
```

## Content

```js
await expect(locator).toHaveText('Submit');
await expect(locator).toHaveText(/welcome/i);       // regex
await expect(locator).toContainText('partial');
await expect(locator).toHaveValue('current value');
await expect(locator).toHaveValues(['opt1', 'opt2']); // multi-select
await expect(locator).toHaveCount(3);
```

## Attributes and CSS

```js
await expect(locator).toHaveAttribute('href', '/home');
await expect(locator).toHaveAttribute('disabled');    // presence only
await expect(locator).toHaveClass('active');
await expect(locator).toContainClass('btn');
await expect(locator).toHaveId('submit');
await expect(locator).toHaveCSS('color', 'rgb(0, 0, 0)');
```

## Accessibility

```js
await expect(locator).toHaveAccessibleName('Close dialog');
await expect(locator).toHaveRole('button');
```

## Page-level

```js
await expect(page).toHaveURL('/dashboard');
await expect(page).toHaveURL(/.*dashboard/);
await expect(page).toHaveTitle('Home');
```

## Snapshots

```js
await expect(locator).toHaveScreenshot('baseline.png');
await expect(locator).toMatchAriaSnapshot(`
  - heading "Sign in"
  - textbox "Email"
`);
```

## Common AI mistakes

```js
// WRONG: non-retrying assertion (snapshot in time, fails on async)
expect(await page.getByText('Done').isVisible()).toBe(true);

// CORRECT: web-first assertion (auto-retries)
await expect(page.getByText('Done')).toBeVisible();

// WRONG: .not.toBeVisible() for hidden check
await expect(locator).not.toBeVisible();

// CORRECT
await expect(locator).toBeHidden();

// WRONG: only checking happy path
// CORRECT: always cover boundary and failure cases as separate test() blocks
```
