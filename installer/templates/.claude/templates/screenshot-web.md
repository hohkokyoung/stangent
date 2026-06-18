# Screenshot — Web (Playwright) Capture Procedure

Used by `/agentic-screenshot` when `test_framework = playwright`. Execute this after Steps 1–4 of the main command.

---

## Capture (one page at a time)

For each page, in order:

1. `mcp__playwright__browser_navigate(url=base_url + route)`
2. `mcp__playwright__browser_snapshot()` — wait until the accessibility tree is populated; confirms the page has rendered.
3. **Verify the URL landed correctly.** Call `mcp__playwright__browser_evaluate(script="window.location.href")` and confirm the result matches `base_url + route`. If it differs (e.g. redirected to login), log the page as failed and continue — do NOT screenshot the wrong page.
4. **Scroll to reveal lazy-loaded content.**
   ```js
   window.scrollTo(0, document.body.scrollHeight);
   ```
   Call `browser_snapshot()` once (acts as the ~500 ms wait), then scroll back to top:
   ```js
   window.scrollTo(0, 0);
   ```
5. Create the page subfolder: `mkdir -p docs/screenshots/<timestamp>/<slug>/`
6. For each viewport (desktop first, mobile second if selected):
   a. `mcp__playwright__browser_resize(width, height)`
   b. `mcp__playwright__browser_screenshot(name="<slug>-<viewport>")`
   c. Move the saved file to `docs/screenshots/<timestamp>/<slug>/<viewport>.png`

Output filenames (inside the page subfolder):
- `docs/screenshots/<timestamp>/home/desktop.png`
- `docs/screenshots/<timestamp>/home/mobile.png`
- `docs/screenshots/<timestamp>/login/desktop.png`

If `browser_navigate`, URL verification, or `browser_screenshot` errors on a page, print `✗  <index>-<slug> — failed: <error>` and continue. Do NOT abort the run.

---

## Index file

Write `docs/screenshots/<timestamp>/README.md`.

**Two viewports:**
```markdown
# Screenshots — <project name>

Captured: <ISO 8601 timestamp>
Framework: playwright
Viewports: Desktop 1280×800 · Mobile 390×844

## Pages

| # | Route | Desktop | Mobile |
|---|-------|---------|--------|
| 1 | `/` | ![](home/desktop.png) | ![](home/mobile.png) |
| 2 | `/login` | ![](login/desktop.png) | ![](login/mobile.png) |
```

**Desktop only:**
```markdown
## Pages

| # | Route | Screenshot |
|---|-------|------------|
| 1 | `/` | ![](home/desktop.png) |
```

Omit any page that failed.

---

## Report

```
Screenshots saved to docs/screenshots/<timestamp>/

✓  home/desktop.png
✓  home/mobile.png
✓  login/desktop.png
✗  dashboard — failed: redirected to /login (not captured)

Index: docs/screenshots/<timestamp>/README.md
```
