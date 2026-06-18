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

Each page gets its own subfolder: `docs/screenshots/<timestamp>/<slug>/`. Create each subfolder just before capturing that page.

### Step 5 — Capture screenshots, write index, and report

Follow the procedure for the detected framework:

- **Web (Playwright):** see `.claude/templates/screenshot-web.md`
- **Mobile (Maestro):** see `.claude/templates/screenshot-mobile.md`

Each template covers: per-page/screen capture loop, index file format, and the final report to print.

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
