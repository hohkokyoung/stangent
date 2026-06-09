# SKILL: playwright

## Purpose
Governs how the tester runs browser UI tests using the **Playwright MCP server**. Scope: browser-based UI verification for web projects. Does NOT cover unit tests, API tests, or mobile native flows.

## HARD GATE — enforce before any other step

**PROHIBITED — you may not do any of the following until browser exploration is complete:**
- Call `Write` or `Edit` to create or modify a `.spec.ts` file
- Generate spec content from the task description or your own knowledge
- Run `npx playwright test` or any Playwright CLI command

**REQUIRED order — no deviation:**
1. `mcp__playwright__browser_navigate` — open the live app in a real browser
2. `mcp__playwright__browser_snapshot` — read the actual accessibility tree
3. `mcp__playwright__browser_click` / `browser_fill` — interact with real elements
4. `mcp__playwright__browser_screenshot` — capture evidence of each state
5. Repeat 2–4 until the full flow is covered
6. **Only then:** write the `.spec.ts` using what you actually observed
7. Run `npx playwright test <file>` and record results

If you are about to call `Write` for a spec file and have not yet called `browser_navigate` — stop. Call `browser_navigate` first.

Generating a spec from the task description alone is a **protocol violation**. The spec must reflect what the live app actually renders, not what the task says it should render.

---

## Rules

1. **MCP-first, artifact-second.** The hard gate above is non-negotiable.
2. **One retrieve() call.** Already handled by the tester role — do not call it again.
3. **Screenshot on every meaningful state transition.** After each action that changes visible UI: `browser_screenshot`. Attach paths to `## Test results`.
4. **Accessibility-first locators.** In the generated spec, prefer `getByRole`, `getByLabel`, `getByPlaceholder` over CSS selectors or XPath.
5. **Never headless.** Playwright MCP runs headed by default. Do not override to headless — the user must be able to watch.
6. **Artifact into the project's test directory.** Find the existing convention (`tests/`, `e2e/`, `__tests__/`). Use `tests/e2e/` if none exists.
7. **Happy path → boundary → failure.** Cover all three. Each is a separate `test()` block.
8. **Only navigate to URLs from the running app.** Use the URL from the task file or what you discover via snapshot. Do not invent URLs.

## Patterns

### MCP exploration loop
```
browser_navigate(url)
→ browser_snapshot()             # read accessibility tree — discover actual elements
→ browser_fill(ref, value)       # enter data using refs from snapshot
→ browser_click(ref)             # interact using refs from snapshot
→ browser_screenshot()           # capture state
→ browser_snapshot()             # verify new state
→ repeat until flow complete
→ THEN write .spec.ts
```

### Generated .spec.ts shape
```typescript
import { test, expect } from '@playwright/test';

test.describe('<feature name>', () => {
  test('happy path — <intent>', async ({ page }) => {
    await page.goto('/path');
    await page.getByLabel('Email').fill('user@example.com');
    await page.getByRole('button', { name: 'Submit' }).click();
    await expect(page.getByText('Success')).toBeVisible();
    await expect(page).toHaveURL('/dashboard');
  });

  test('boundary — <edge case>', async ({ page }) => { /* ... */ });

  test('failure — <error case>', async ({ page }) => { /* ... */ });
});
```

### Viewing the report
```bash
npx playwright show-report
```
Opens HTML report with video, screenshots, and trace per test.

## Planner hints

Before finalising task decomposition, check for cross-page scope gaps:
- Does a state change from this feature need to be reflected on other pages or routes?
- Can any page in this feature be reached directly via URL (shared link, browser back, reload)?
- Do any existing list or collection views need to reflect this feature's state changes?
- Are there keyboard navigation, focus management, or accessibility implications?

Any "yes" is an in-scope requirement. Surface it in requirements — do not resolve how.

## Anti-patterns

- Writing `.spec.ts` before `browser_navigate` — protocol violation, see HARD GATE.
- Generating locators from the task description — use refs from `browser_snapshot` output.
- `page.locator('.btn-primary')` — fragile CSS. Use role/label locators.
- Only asserting the happy path — boundary and failure cases are required.
- Running headless (`--headed=false`) — defeats the purpose of live exploration.
- `browser_screenshot` only at the end — screenshot every meaningful step.
