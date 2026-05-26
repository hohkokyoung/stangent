Show features that need your attention — paused, awaiting confirmation, blocked, escalated.

Usage: /inbox

Like /status but filtered to only items where you have to do something.
Use this as a quick "what's waiting on me?" check.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir   = config.paths.feature_dir
  - registry_path = config.paths.registry_path

## Step 2 — Find actionable features

Read the registry. Collect features whose status is one of:
  - AWAITING_CONFIRMATION
  - PAUSED
  - BLOCKED
  - ESCALATED

For each, also read the feature file to get:
  - title
  - tier
  - active.json `state` (for context — which stage is active)
  - any DECISION REQUIRED block

## Step 3 — Output

If nothing actionable:
  Output: "Inbox zero. No features waiting on you."
  Stop.

Otherwise, group by urgency:

```
━━━ INBOX — features waiting on you ━━━━━━━━━━━━━━

NEEDS DECISION ({n})
  FEAT-XXX  Title  [tier]
    Question: {DECISION REQUIRED summary or 'review spec'}
    Resume:   /resume FEAT-XXX

PAUSED ({n})
  FEAT-XXX  Title  [tier]
    Paused at: {state} ({timestamp})
    Why:       {active.json state, or 'unknown'}
    Resume:    /resume FEAT-XXX

ESCALATED ({n})
  FEAT-XXX  Title
    Retries:  {retry_count}
    Reason:   {short reason from ## Review findings}
    Recover:  see {feature_file_path}

BLOCKED ({n})
  FEAT-XXX  Title
    Waiting on: {comma-separated dependency FEAT-IDs}
```

## Step 4 — Suggest next action

If exactly 1 item: output "Tip: /resume {feature_id} to handle it now."
Otherwise: output the count and "Run /resume <FEAT-ID> for the one you want to tackle first."
