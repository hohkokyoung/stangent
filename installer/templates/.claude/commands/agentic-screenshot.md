---
description: Walk every page/screen in the running app and save screenshots to docs/screenshots/<date>/ for documentation and portfolio reference.
argument-hint: "[all | <page-slugs>]"
---

# /agentic-screenshot

Capture screenshots of every page or screen in the app. Saves to `docs/screenshots/<date-time>/` in the project root — ready for a README, portfolio, or design docs.

## Usage

| Command | What it does |
|---|---|
| `/agentic-screenshot` | Interactive — asks which pages/screens to capture |
| `/agentic-screenshot all` | Auto-discover all routes/screens and capture everything |
| `/agentic-screenshot <slugs>` | Capture specific pages: `/agentic-screenshot home login dashboard` |

---

## Procedure

### Step 1 — Detect framework

Read `.claude/state/project.yml` for `test_framework`. If the file is missing or `test_framework` is `unknown`:

```
[agentic-screenshot] No test framework detected. Run /agentic-index first, then retry.
```

STOP. Do not proceed.

Map framework → MCP:
- `playwright` → `mcp__playwright__*`
- `maestro` → `mcp__maestro__*`
- anything else:

```
[agentic-screenshot] test_framework=<value> is not supported.
Supported: playwright (web), maestro (mobile).
```

STOP. Do not proceed.

### Step 2 — Verify MCP is reachable

**This step is mandatory before any other MCP call. Do not skip it.**

#### Web (Playwright)

Call `mcp__playwright__browser_navigate` with `url="about:blank"`.

If the call throws, times out, or returns an error of any kind:

```
[agentic-screenshot] Playwright MCP did not respond.

To fix:
  1. Confirm "playwright" is in enabledMcpjsonServers in .claude/settings.json
  2. Run: npx -y @playwright/mcp@latest
  3. Restart Claude Code and retry /agentic-screenshot
```

STOP. Do not attempt any further Playwright calls.

#### Mobile (Maestro)

Call `mcp__maestro__list_devices`.

If the call throws, times out, or returns an error of any kind:

```
[agentic-screenshot] Maestro MCP did not respond.

To fix:
  1. Confirm "maestro" is in enabledMcpjsonServers in .claude/settings.json
  2. Confirm Maestro CLI is installed: maestro --version
  3. Start the MCP server: maestro mcp
  4. Restart Claude Code and retry /agentic-screenshot
```

STOP. Do not attempt any further Maestro calls.

If `list_devices` returns an empty device list:

```
[agentic-screenshot] Maestro MCP is running but no device is connected.

Start a simulator or connect a physical device, then retry /agentic-screenshot
```

STOP.

### Step 3 — Resolve pages/screens

**If `$ARGUMENTS` is empty (interactive mode):**

Use `AskUserQuestion` (1 round, up to 3 questions):

1. Which pages or screens do you want to capture? List them (e.g. "home, login, dashboard") or say "all" to capture everything discoverable.
2. *(web only)* What URL is the app running on? (e.g. `http://localhost:3000`)
3. *(web only)* Which viewports? (suggested: Desktop 1280×800 + Mobile 390×844)

**If `$ARGUMENTS` is `all`:**

- Web: scan route files in this order — `src/app/`, `app/`, `src/pages/`, `pages/`. Collect every `page.tsx`, `page.jsx`, `index.tsx`, `index.jsx` that maps to a static route. Skip dynamic segments (`[param]`, `[[...slug]]`, `:param`). Build a route list.
- Mobile: scan navigation config for registered screen names. If no config is found, ask the developer to list screens manually (treat as interactive mode).

**If `$ARGUMENTS` is a slug list:**

Parse as space- or comma-separated slugs. Map each to a route (web: `home` → `/`, `login` → `/login`, others → `/<slug>`) or a screen name (mobile). Ask only if a slug cannot be resolved.

### Step 4 — Prepare output folder

Run:

```bash
mkdir -p docs/screenshots
TIMESTAMP=$(date +%Y-%m-%d_%H-%M)
OUTPUT=docs/screenshots/$TIMESTAMP
mkdir -p "$OUTPUT"
```

### Step 5 — Capture screenshots

Index each page starting at `01`. Use zero-padded two-digit indices.

#### Web (Playwright)

For each page, in order:

1. `mcp__playwright__browser_navigate(url=base_url + route)`
2. `mcp__playwright__browser_snapshot()` — wait until the accessibility tree is populated; this confirms the page has rendered
3. For each viewport (desktop first, mobile second if selected):
   a. `mcp__playwright__browser_resize(width, height)`
   b. `mcp__playwright__browser_screenshot(name="<index>-<slug>-<viewport>")`
   c. Move the saved file to `docs/screenshots/<timestamp>/<index>-<slug>-<viewport>.png`

Filename examples: `01-home-desktop.png`, `01-home-mobile.png`, `02-login-desktop.png`

If `browser_navigate` or `browser_screenshot` returns an error for a page, print:

```
✗  <index>-<slug> — failed: <error message>
```

Continue to the next page. Do NOT stop the entire run for a single page failure.

#### Mobile (Maestro)

The device is already confirmed live from Step 2.

1. `mcp__maestro__launch_app(appId)` — read `appId` from `project.yml`, or from the task file, or ask if neither is available.
2. For each screen, in order:
   a. Navigate to the screen: tap through the minimal path from the app home (use `inspect_view_hierarchy` to confirm the screen before tapping)
   b. `mcp__maestro__inspect_view_hierarchy()` — confirm the target screen is active
   c. `mcp__maestro__take_screenshot(name="<index>-<slug>")` — Maestro saves to a platform-managed path
   d. Copy/move the file to `docs/screenshots/<timestamp>/<index>-<slug>.png`

If any Maestro call fails for a screen, print:

```
✗  <index>-<slug> — failed: <error message>
```

Continue to the next screen.

### Step 6 — Write index file

Write `docs/screenshots/<timestamp>/README.md`.

**Web (two viewports):**

```markdown
# Screenshots — <project name from package.json / pubspec.yaml / directory name>

Captured: <ISO 8601 timestamp>
Framework: playwright
Viewports: Desktop 1280×800 · Mobile 390×844

## Pages

| # | Route | Desktop | Mobile |
|---|-------|---------|--------|
| 1 | `/` | ![](01-home-desktop.png) | ![](01-home-mobile.png) |
| 2 | `/login` | ![](02-login-desktop.png) | ![](02-login-mobile.png) |
```

**Web (desktop only):**

```markdown
## Pages

| # | Route | Screenshot |
|---|-------|------------|
| 1 | `/` | ![](01-home-desktop.png) |
```

**Mobile:**

```markdown
# Screenshots — <project name>

Captured: <ISO 8601 timestamp>
Framework: maestro
Device: <device name from list_devices>

## Screens

| # | Screen | Screenshot |
|---|--------|------------|
| 1 | Home | ![](01-home.png) |
| 2 | Login | ![](02-login.png) |
```

Omit any page/screen that failed from the table.

### Step 7 — Report

Print:

```
Screenshots saved to docs/screenshots/<timestamp>/

✓  01-home-desktop.png
✓  01-home-mobile.png
✓  02-login-desktop.png
✗  03-dashboard-desktop.png — failed: navigate timed out

Index: docs/screenshots/<timestamp>/README.md
```

---

## Rules

- **MCP probe in Step 2 is a hard gate.** If it fails, stop immediately — no screenshots, no folder creation.
- **Single page failure does not abort the run.** Log `✗` and move on.
- **Never invent URLs.** Use only what the user provides or what is found in route files.
- **Never authenticate.** Do not fill login forms or handle auth flows. If a page redirects to login, log it as failed and continue.
- **Leave the app as-is.** Do not close the browser or kill the app after capture.
- **Do not touch `.claude/` state.** This command writes only to `docs/screenshots/`.
- **Do not modify any source file, test file, or configuration.**

---

## After capture

The folder is ready to:
- Reference from `README.md` or `docs/` pages
- Drop into a portfolio or case study
- Use as a visual baseline before running `/agentic-test`
