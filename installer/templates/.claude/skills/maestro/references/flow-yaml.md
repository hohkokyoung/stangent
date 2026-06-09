# Maestro Flow YAML (CLI 2.6.0)

## Structure

```yaml
# Top section: flow configuration (before ---)
appId: com.example.app   # Android: package, iOS: bundle ID
---
# Commands go directly after --- (no 'flow:' key)
- launchApp:
    clearState: true      # wipe state — use at start of each test suite
- tapOn: "Login"
- inputText: "user@example.com"
- tapOn: "Password Field"
- inputText: "secret"
- tapOn: "Submit"
- assertVisible: "Welcome"
```

**Common AI mistake:** Adding a `flow:` key at the top level — it does not exist. Commands go directly after `---`.

## tapOn — all selector options

```yaml
# By visible text
- tapOn: "Submit"

# By accessibility ID / resource-id
- tapOn:
    id: "login_button"

# By accessibility description
- tapOn:
    description: "Close dialog"

# By coordinate (use percentages — never hardcode pixels)
- tapOn:
    point: "50%,75%"

# When multiple elements match — 0-based index
- tapOn:
    text: "Delete"
    index: 2

# Long press
- longPressOn: "Item to long press"
```

**Common AI mistake:** Using `testID` as a key — it does not exist. React Native's `testID` maps to accessibility ID, which Maestro reads as `id`.

## Input and interaction

```yaml
- inputText: "Hello World"
- inputText: ${email}          # from environment or runScript output

- pressKey: Enter
- pressKey: Back
- pressKey: Home
- pressKey: Delete

- swipe:
    direction: UP              # UP | DOWN | LEFT | RIGHT
    duration: 400              # ms

- scrollUntilVisible:
    element:
      text: "Terms of Service"
    direction: DOWN
    timeout: 50000

- takeScreenshot: "after_login"
- waitForAnimationToEnd
```

## Flow reuse and environment variables

```yaml
appId: com.example.app
---
- runFlow: ./flows/login.yaml
- runFlow:
    path: ./flows/create_post.yaml
    env:
      EMAIL: ${TEST_EMAIL}
      PASSWORD: ${TEST_PASSWORD}
    when:
      platform: android
```

## JavaScript / runScript

```yaml
- runScript: |
    const timestamp = Date.now()
    output.email = `user_${timestamp}@example.com`

- inputText: ${email}
```

Use GraalJS syntax (ECMAScript 2022). `var` is not supported in CLI 2.6.0+ (Rhino engine removed).

## Mistake cheatsheet

| AI generates | Correct |
|---|---|
| `flow:` key in YAML | No `flow:` key — commands go directly after `---` |
| `testID: "btn"` / `resourceId: "btn"` | `tapOn:\n    id: "btn"` |
| `- sleep: 3000` | `- extendedWaitUntil:\n    visible:\n      text: "..."\n    timeout: 3000` |
| `clearState: false` on first flow | `clearState: true` in first flow of a suite |
| Pixel coordinates `point: "320,480"` | Use percentages `"50%,75%"` |
| Flutter Key selector | Use `Semantics` widget with `identifier` prop |
| SwiftUI: no accessibility label | Add `.accessibilityIdentifier("id")` in SwiftUI |

## Breaking changes (CLI 2.6.0)

- Rhino JS engine removed — GraalJS only
- Maestro Studio removed from CLI (now separate desktop app)
- `--shard N` deprecated → use `--shard-split N` or `--shard-all N`
