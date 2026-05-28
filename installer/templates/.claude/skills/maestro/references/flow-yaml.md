# Maestro flow YAML

A flow file is a YAML file with an `appId` header and a list of commands.

## Basic structure

```yaml
appId: com.example.myapp
---
- launchApp
- assertVisible: "Welcome"
- tapOn: "Get Started"
```

## Header fields

```yaml
appId: com.example.myapp        # required — bundle ID / package name
name: Login happy path          # optional — shown in reports
tags:                           # optional — for filtering
  - auth
  - smoke
---
```

---

## Navigation commands

```yaml
- launchApp                             # launch from cold
- launchApp:
    clearState: true                    # clear app data before launch
- tapOn: "Button label"                 # tap by visible text or accessibility label
- tapOn:
    text: "Submit"
    index: 0                            # if multiple matches, pick by index
- longPressOn: "Item"
- doubleTapOn: "Image"
- back                                  # Android back / iOS navigation back
- scroll                                # scroll down
- scrollUntilVisible:
    element:
      text: "Load more"
    direction: DOWN
```

## Text input

```yaml
- tapOn: "Email"                        # focus the field first
- inputText: "user@example.com"
- clearText                             # clear focused field
- tapOn: "Password"
- inputText: "secret123"
- hideKeyboard                          # dismiss keyboard
```

## Assertions

```yaml
- assertVisible: "Dashboard"            # element with this text is on screen
- assertNotVisible: "Error"
- assertVisible:
    text: "Welcome back"
    enabled: true                       # also check it's tappable
- assertTrue: ${element.text == "42"}   # expression assertion
```

## Screenshots

```yaml
- takeScreenshot: login_success         # saves to .maestro/screenshots/
```

## Waits

Maestro has built-in tolerance — it retries taps/assertions automatically. Only add explicit waits when truly needed:
```yaml
- waitForAnimationToEnd
- extendedWaitUntil:
    visible:
      text: "Loaded"
    timeout: 5000                       # ms
```

## Conditionals

```yaml
- runFlow:
    when:
      visible: "Onboarding"
    file: ./flows/skip_onboarding.yaml
```

## Subflows

```yaml
- runFlow: ./flows/shared/login.yaml   # reuse across test files
```

---

## File naming convention

```
.maestro/
  <feature>/
    <task_id>_happy.yaml
    <task_id>_boundary.yaml
    <task_id>_failure.yaml
  shared/
    login.yaml        # reusable setup flows
    logout.yaml
```

---

## Running

```bash
maestro test .maestro/<feature>/<task_id>_happy.yaml
maestro test .maestro/<feature>/          # run all flows in folder
```
