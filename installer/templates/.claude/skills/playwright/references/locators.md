# Playwright locators

When generating `.spec.ts` from MCP interactions, use these locators in priority order.

## Priority order (most to least preferred)

### 1. Role-based (best)
```typescript
page.getByRole('button', { name: 'Submit' })
page.getByRole('textbox', { name: 'Email' })
page.getByRole('heading', { name: 'Dashboard' })
page.getByRole('link', { name: 'Sign in' })
page.getByRole('checkbox', { name: 'Remember me' })
```
Matches the accessibility tree. Survives style refactors.

### 2. Label-based
```typescript
page.getByLabel('Email address')
page.getByLabel('Password')
```
Matches `<label>` text or `aria-label`. Very stable.

### 3. Placeholder
```typescript
page.getByPlaceholder('Search...')
```

### 4. Text content
```typescript
page.getByText('Welcome back')
page.getByText('Error: invalid email', { exact: true })
```
Use `exact: true` when the text could be a substring of something else.

### 5. Test ID (when role/label are not available)
```typescript
page.getByTestId('submit-btn')
```
Requires `data-testid` attribute in the HTML. Ask the implementer to add these if needed.

### 6. CSS selector (last resort)
```typescript
page.locator('.error-message')
```
Fragile — breaks on style changes. Only use when no semantic alternative exists.

---

## Scoping

Narrow scope to a container before locating:
```typescript
const form = page.getByRole('form', { name: 'Login' });
await form.getByLabel('Email').fill('user@example.com');
await form.getByRole('button', { name: 'Submit' }).click();
```

## Chaining

```typescript
page.getByRole('listitem').filter({ hasText: 'John Doe' }).getByRole('button', { name: 'Delete' })
```

## Waiting

Playwright auto-waits. Do NOT add manual `page.waitForTimeout()`. If you need to wait for a specific element:
```typescript
await expect(page.getByRole('status')).toBeVisible();
```
