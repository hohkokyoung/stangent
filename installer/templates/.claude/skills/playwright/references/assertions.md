# Playwright assertions

All assertions use `expect()`. Playwright auto-retries until timeout (default 5s). Do NOT add manual waits before assertions.

## Visibility

```typescript
await expect(locator).toBeVisible();
await expect(locator).toBeHidden();
```

## Text content

```typescript
await expect(locator).toHaveText('Exact text');
await expect(locator).toContainText('partial');
await expect(locator).toHaveText(/regex pattern/);
```

## Input state

```typescript
await expect(locator).toHaveValue('filled value');
await expect(locator).toBeEmpty();
await expect(locator).toBeChecked();
await expect(locator).toBeDisabled();
await expect(locator).toBeEnabled();
await expect(locator).toBeFocused();
```

## Count

```typescript
await expect(locator).toHaveCount(3);
```

## URL

```typescript
await expect(page).toHaveURL('/dashboard');
await expect(page).toHaveURL(/.*\/dashboard/);
```

## Page title

```typescript
await expect(page).toHaveTitle('Dashboard | MyApp');
```

## Attribute

```typescript
await expect(locator).toHaveAttribute('aria-expanded', 'true');
await expect(locator).toHaveClass(/active/);
```

---

## Soft assertions (non-blocking)

When you want to collect multiple failures in one run:
```typescript
await expect.soft(locator).toBeVisible();
await expect.soft(page).toHaveURL('/dashboard');
// test continues even if one fails
```

---

## Negative assertions

```typescript
await expect(locator).not.toBeVisible();
await expect(locator).not.toContainText('Error');
```

---

## Required coverage per task

| Case | Assertions to include |
|---|---|
| Happy path | Final URL, success message visible, key data rendered |
| Boundary | Edge input accepted/rejected correctly, count assertions |
| Failure | Error message visible, form not submitted, URL unchanged |
