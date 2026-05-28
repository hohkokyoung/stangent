# Maestro assertions

Maestro assertions check the visible state of the screen using the accessibility layer. All assertions are retried until they pass or timeout (default 5s).

## Visibility

```yaml
- assertVisible: "Text on screen"
- assertNotVisible: "Error message"
```

By text with options:
```yaml
- assertVisible:
    text: "Submit"
    enabled: true           # also check it's interactable
    checked: false          # for checkboxes/toggles
```

---

## Element state

```yaml
- assertVisible:
    text: "Save"
    enabled: false          # assert it's disabled (grayed out)

- assertVisible:
    text: "Remember me"
    checked: true           # assert checkbox is checked
```

---

## Expression assertions

For dynamic values (counters, computed text):
```yaml
- assertTrue: ${element.text == "3 items"}
- assertTrue: ${element.text.startsWith("Hello")}
```

Get an element value into a variable first:
```yaml
- copyTextFrom: "Cart count"      # copies visible text to clipboard
- assertTrue: ${maestro.copiedText == "2"}
```

---

## Screen-level assertions

```yaml
- assertVisible: "Dashboard"       # presence of key screen landmark
- assertNotVisible: "Loading..."   # loading state cleared
- assertNotVisible: "Sign In"      # navigation succeeded (login screen gone)
```

---

## Required coverage per task

| Case | What to assert |
|---|---|
| Happy path | Success state visible, error state not visible, correct screen reached |
| Boundary | Edge input accepted (or rejected) correctly, button state correct |
| Failure | Error message visible, app does not crash, recoverable state |

---

## Taking screenshots alongside assertions

Always pair assertions with screenshots so failures are visually traceable:
```yaml
- assertVisible: "Dashboard"
- takeScreenshot: after_login

- assertNotVisible: "Error"
- takeScreenshot: no_error_state
```

Screenshot files are saved to `.maestro/screenshots/` and referenced in `## Test results`.
