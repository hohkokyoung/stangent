# Playwright MCP tools

The Playwright MCP server (`@playwright/mcp`) exposes browser automation as MCP tools. All tools are called via `mcp__playwright__<tool>`.

## Navigation

### `browser_navigate`
```
browser_navigate(url: string)
```
Navigates to a URL. Always call this first. Use the dev server URL from the task file.

### `browser_go_back` / `browser_go_forward`
```
browser_go_back()
browser_go_forward()
```

---

## Reading the page

### `browser_snapshot`
```
browser_snapshot()
```
Returns the **accessibility tree** — preferred over screenshot for finding elements. Use this to discover roles, labels, and text before clicking. Call after every navigation or major state change.

### `browser_screenshot`
```
browser_screenshot()
```
Returns a base64 screenshot. Call after every meaningful state transition. Save path to `## Test results`.

---

## Interaction

### `browser_click`
```
browser_click(element: string, ref: string)
```
`element` is a human-readable description. `ref` is the ref ID from `browser_snapshot` output. Always snapshot first, then click using the ref.

### `browser_fill`
```
browser_fill(element: string, ref: string, value: string)
```
Types into an input. Get `ref` from snapshot.

### `browser_select_option`
```
browser_select_option(element: string, ref: string, values: string[])
```

### `browser_check` / `browser_uncheck`
```
browser_check(element: string, ref: string)
browser_uncheck(element: string, ref: string)
```

### `browser_press_key`
```
browser_press_key(key: string)
```
e.g. `"Enter"`, `"Tab"`, `"Escape"`.

### `browser_hover`
```
browser_hover(element: string, ref: string)
```

---

## Evaluation

### `browser_evaluate`
```
browser_evaluate(expression: string)
```
Runs JS in the page context. Use sparingly — prefer accessibility-tree interactions.

---

## Network / Console

### `browser_network_requests`
```
browser_network_requests()
```
Returns recent XHR/fetch requests. Useful to verify API calls were made.

### `browser_console_messages`
```
browser_console_messages()
```
Returns console output. Check for errors after interactions.

---

## Tabs

### `browser_tab_new` / `browser_tab_close` / `browser_tab_list`
Manage tabs when testing multi-tab flows.

---

## Workflow pattern

```
1. browser_navigate(url)
2. browser_snapshot()          → find element refs
3. browser_fill(ref, value)    → enter data
4. browser_click(ref)          → submit / interact
5. browser_screenshot()        → capture result
6. browser_snapshot()          → verify new state
7. repeat 3-6 for each step
8. browser_console_messages()  → check for errors
```
