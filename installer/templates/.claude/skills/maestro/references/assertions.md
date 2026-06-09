# Maestro Assertions (CLI 2.6.0)

## assertVisible / assertNotVisible

```yaml
# Element must be visible
- assertVisible: "Welcome back"

# Both text AND id must match
- assertVisible:
    text: "Welcome back"
    id: "welcome_message"

# Element must NOT be visible
- assertNotVisible: "Error message"
- assertNotVisible:
    text: "Loading..."
```

Maestro does **partial text matching** by default — `assertVisible: "Order #"` matches `"Order #12345"`.

## assertTrue — custom condition

```yaml
- assertTrue:
    condition: ${username == "alice"}
    label: "Username should be alice"   # shown in output on failure
```

## AI assertions (v1.39.0+, requires Maestro Cloud API key)

```yaml
- assertWithAI: "The profile picture of a user named Alice is visible"
- assertNoDefectsWithAI   # visual regression without baseline
```

## Wait for slow elements

```yaml
# Preferred: element-based wait (not arbitrary sleep)
- extendedWaitUntil:
    visible:
      text: "Dashboard"
    timeout: 15000    # ms

# AI commonly generates sleep — avoid it
# - sleep: 3000  ← WRONG, causes flakiness
```

Maestro auto-retries until timeout — `tapOn` waits automatically. Only use `extendedWaitUntil` for genuinely slow operations (e.g., loading screens, network-dependent content).

## Screenshot for evidence

```yaml
- takeScreenshot: "after_login"
- assertVisible: "Welcome"
- takeScreenshot: "welcome_shown"
```

## Behaviour difference: tapOn vs assertVisible on missing element

- `assertVisible` — **throws immediately** if element not found by timeout
- `tapOn` — **waits silently** until timeout, then throws

Use `assertVisible` before `tapOn` when timing matters or you need explicit failure messages.
