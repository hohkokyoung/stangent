Manage gateway enforcement — inspect the active contract, unblock a path, or pause enforcement.

Usage:
  /gateway status              — show active feature + contract summary
  /gateway unblock <path>      — add a path to allowed_paths for the active feature
  /gateway pause               — temporarily disable enforcement (removes active.json, logs reason)
  /gateway resume              — re-enable enforcement (restores active.json from feature file)

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - contracts_dir  = config.paths.contracts_dir  (default: ".stangent/contracts/")
  - gateway_dir    = ".stangent/gateway/"
  - log_dir        = config.paths.log_dir

---

## STATUS MODE

If "$ARGUMENTS" is "status" or empty:

Read `.stangent/gateway/active.json`.
If not found: output "No active feature. Gateway is in permissive mode." and stop.

Read `.stangent/contracts/{feature_id}.json`.
If not found: output "Active feature: {feature_id} — no contract written yet." and stop.

Output:
```
Gateway status — {feature_id} ({state}) — agent: {agent}

allowed_paths ({N}):
  {each path, one per line}

blocked_paths ({N}):
  {each path, one per line}

bash_blocklist ({N}):
  {each entry, one per line, or "none"}

capabilities ({N agents}):
  {agent}: {capabilities list}

Audit log: {log_dir}/gateway_audit.jsonl
```

---

## UNBLOCK MODE

If "$ARGUMENTS" starts with "unblock ":

  path_to_add = "$ARGUMENTS" with "unblock " stripped from the front.

  Read `.stangent/gateway/active.json`.
  If not found: output "No active feature. Nothing to unblock." and stop.

  Read `.stangent/contracts/{feature_id}.json`.
  If not found: output "No contract for {feature_id}. Run /plan first." and stop.

  Check if path_to_add is already in allowed_paths:
  - If yes: output "Already in allowed_paths: {path_to_add}" and stop.

  Ask for confirmation before writing:
  ```
  ⚠ Unblock {path_to_add} for {feature_id}?
  This path is not in ## Files to Touch — adding it bypasses the spec you confirmed.
  Type "yes" to proceed, anything else to cancel.
  ```
  Wait for response. If not "yes": output "Unblock cancelled." and stop.

  Add path_to_add to contract.allowed_paths.
  Write the updated contract back to `.stangent/contracts/{feature_id}.json`.

  Append to `.stangent/logs/gateway_audit.jsonl`:
  ```json
  {"ts": "...", "feature_id": "...", "action": "override", "target": "{path_to_add}",
   "reason": "developer unblock via /gateway", "agent": "developer"}
  ```

  Also append to the feature file's `## Pipeline History`:
  `[timestamp] | GATEWAY_OVERRIDE | developer | unblocked: {path_to_add}`

  Output:
  ```
  ✓ Unblocked: {path_to_add}
  Added to allowed_paths in contracts/{feature_id}.json.
  The implementer can now write to this path.
  ```

---

## PAUSE MODE

If "$ARGUMENTS" is "pause":

  Read `.stangent/gateway/active.json`.
  If not found: output "Gateway is already inactive." and stop.

  Ask: "Pause gateway enforcement for {feature_id}?
  This disables all path and agent checks until you run /gateway resume.
  Hard bash blocks (git push --force, rm -rf, etc.) remain active.
  Reason (required): "

  Wait for reason. If empty or no response: output "Reason required to pause gateway." and stop.

  Copy active.json content to `.stangent/gateway/active.json.paused`.
  Delete `.stangent/gateway/active.json`.

  Append to `.stangent/logs/gateway_audit.jsonl`:
  ```json
  {"ts": "...", "feature_id": "...", "action": "pause", "reason": "{developer reason}",
   "agent": "developer"}
  ```

  Output:
  ```
  ⚠ Gateway paused for {feature_id}.
  Reason: {reason}
  Hard bash blocks remain active.
  Run /gateway resume to re-enable enforcement.
  ```

---

## RESUME MODE

If "$ARGUMENTS" is "resume":

  Check for `.stangent/gateway/active.json.paused`.
  If not found: output "No paused gateway session found." and stop.

  Copy `.stangent/gateway/active.json.paused` → `.stangent/gateway/active.json`.
  Delete `.stangent/gateway/active.json.paused`.

  Append to `.stangent/logs/gateway_audit.jsonl`:
  ```json
  {"ts": "...", "feature_id": "...", "action": "resume", "agent": "developer"}
  ```

  Output:
  ```
  ✓ Gateway enforcement resumed for {feature_id}.
  ```
