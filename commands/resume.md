Resume a paused or interrupted feature from where it left off.

Usage: /resume <FEAT-ID>
Example: /resume FEAT-003

Reads the feature file status and Pipeline History, then routes to the
correct pipeline stage automatically. You never need to remember which
stage to re-enter.

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - feature_dir  = config.paths.feature_dir
  - archive_dir  = config.paths.archive_dir
  - log_dir      = config.paths.log_dir
  - max_retries  = config.pipeline.max_retries
  - config_path  = (absolute path to .stangent/config.json)

---

## Step 2 — Load feature

If "$ARGUMENTS" is empty: output "Usage: /resume <FEAT-ID>" and stop.

Find the feature file: glob {feature_dir}/$ARGUMENTS*.md
Also check {archive_dir}/$ARGUMENTS*.md.

If not found: output "Feature $ARGUMENTS not found in features/ or archive/." and stop.

Read frontmatter: title, status, branch, retry_count.
Read ## Pipeline History — find the most recent entry to understand
where the feature was when it paused.

---

## Step 3 — Route by status

### PAUSED

Read the last `## Pipeline History` entry to identify which stage was
active when PAUSED.

Also check whether a DECISION REQUIRED block exists in the feature file.
If yes, display it and ask the developer to answer before resuming.
Once answered, append the answer to ## Pipeline History:
  `[timestamp] | DECISION_ANSWERED | developer | {answer summary}`

Route as follows:

**Paused during PLANNING:**
  Output:
    "Resuming planning for {feature_id} — {title}
     Last pause: {timestamp from Pipeline History}
     {DECISION REQUIRED block if present, or 'No pending decisions.'}"

  Set status = PLANNING.
  Append to ## Pipeline History: `[timestamp] | PLANNING | orchestrator | /resume — replanning`
  Spawn the planner using the Agent tool:
    INPUTS:
    {
      "feature_id":        "$ARGUMENTS",
      "feature_file_path": "(resolved path)",
      "config_path":       "(absolute path to .stangent/config.json)",
      "extra": { "raw_request": "(title from feature file frontmatter)" }
    }
    INSTRUCTIONS:
    Read the full contents of: .claude/agents/stangent-planner.md
    Then execute those instructions using the inputs above.
  On SPEC_WRITTEN: set status = AWAITING_CONFIRMATION, present spec summary, stop.
  On PAUSED: output new resume instructions. Stop.
  On FAILED: output failure. Stop.

**Paused during IMPLEMENTING or after CONFIRMED:**
  Output:
    "Resuming implementation for {feature_id} — {title}
     Retry: {retry_count}/{max_retries}
     Last pause: {timestamp from Pipeline History}
     {DECISION REQUIRED block if present, or 'No pending decisions.'}"

  Set status = IMPLEMENTING.
  Append to ## Pipeline History: `[timestamp] | IMPLEMENTING | orchestrator | /resume`
  Read retry_count from frontmatter. If > 0: read ## Review Verdict for previous_verdict.
  Spawn the orchestrator using the Agent tool to run implement → review → SRS
  in a fresh context:
    INPUTS:
    {
      "feature_id":  "$ARGUMENTS",
      "config_path": "(absolute path to .stangent/config.json)"
    }
    INSTRUCTIONS:
    Read the full contents of: .claude/agents/stangent.md
    The feature was paused during IMPLEMENTING. Resume from STEP 5.
    feature_id is $ARGUMENTS. config_path is (absolute path).
  Wait for result and output final status.

**Paused during REVIEWING:**
  Output:
    "Resuming review for {feature_id} — {title}
     Last pause: {timestamp from Pipeline History}"

  Set status = REVIEWING.
  Append to ## Pipeline History: `[timestamp] | REVIEWING | orchestrator | /resume`
  Spawn the orchestrator using the Agent tool to run review → SRS → complete:
    INPUTS:
    {
      "feature_id":  "$ARGUMENTS",
      "config_path": "(absolute path to .stangent/config.json)"
    }
    INSTRUCTIONS:
    Read the full contents of: .claude/agents/stangent.md
    The feature was paused during REVIEWING. Resume from STEP 6.
    feature_id is $ARGUMENTS. config_path is (absolute path).
  Wait for result and output final status.

**Paused during SRS_UPDATE:**
  Output:
    "Resuming SRS update for {feature_id} — {title}"

  Read .claude/agents/stangent.md.
  Execute orchestrator from STEP 7 (SRS_UPDATE).

---

### AWAITING_CONFIRMATION

The spec is written but not yet approved. Show it now:

Output:
  "Feature {feature_id} is waiting for your confirmation.

   {## Scope content}

   Acceptance Criteria:
   {## Acceptance Criteria content}

   Out of Bounds:
   {## Out of Bounds content}

   Files to Touch:
   {## Files to Touch content}

   Type 'yes' to confirm and begin implementation, 'edit' to revise the spec,
   or 'abort' to abandon this feature."

Wait for response:
- "yes"  → set status = CONFIRMED, proceed to IMPLEMENTING (as above, retry_count = 0).
- "edit" → re-run planner with the developer's correction note. Stop after SPEC_WRITTEN.
- "abort"→ output "Run /abandon {feature_id} to cleanly cancel." and stop.

---

### CONFIRMED

Implementation has not started yet. Proceed directly to IMPLEMENTING (as above).

---

### ESCALATED

Output:
  "Feature {feature_id} was escalated — it needs manual intervention before resuming.

   Feature file: {feature_file_path}

   Read the ## Review Verdict or ## Pipeline History to see what failed.
   Fix the issue, then:
     - Set status = CONFIRMED in the frontmatter
     - Re-run /resume {feature_id}"

Stop.

---

### BLOCKED

Read ## Depends On. For each listed feature ID:
  - Find its file
  - Read its status

Output:
  "Feature {feature_id} is blocked on:
   {each dependency: FEAT-XXX — {title} — status: {status}}"

Output: "Complete the blocking features first, then re-run /resume {feature_id}."
Stop.

---

### REFINING

The feature is mid-refinement — a /refine was in progress and paused.

Check the last Pipeline History entry for the feedback that was being processed.

Output:
  "Resuming refinement for {feature_id} — {title}
   Original feedback: {feedback from /refine Pipeline History entry}"

Set status = REFINING.
Update active.json: `{ ..., "state": "REFINING", "agent": "planner" }`
Append to Pipeline History: `[timestamp] | REFINING | orchestrator | /resume — re-entering revision`

Spawn the planner using the Agent tool in Revision Mode:
  INPUTS:
  {
    "feature_id":        "{feature_id}",
    "feature_file_path": "(resolved path)",
    "config_path":       "(absolute path to .stangent/config.json)",
    "extra": { "revision_context": "(feedback from Pipeline History entry)" }
  }
  INSTRUCTIONS:
  Read the full contents of: .claude/agents/stangent-planner.md
  Then execute those instructions using the inputs above.
  revision_context is set — enter Revision Mode (Phase 0).

On SPEC_REVISED:  follow /refine Step 7 (SPEC_REVISED) logic.
On SPEC_UNCHANGED: follow /refine Step 7 (SPEC_UNCHANGED) logic.
On PAUSED:        output new resume instructions. Stop.
On FAILED:        output failure. Stop.

---

### PLANNING (without PAUSED)

The planner may have been interrupted mid-run. Output:
  "Feature {feature_id} is mid-planning. The planner may have been interrupted.
   If planning is stuck, set status = CREATED in the frontmatter and run /plan {feature_id}.
   If the spec looks complete, set status = AWAITING_CONFIRMATION and re-run /resume {feature_id}."
Stop.

---

### IMPLEMENTING (without PAUSED)

Output:
  "Feature {feature_id} is already mid-implementation.
   If the implementer was interrupted, set status = PAUSED in the frontmatter
   and re-run /resume {feature_id}."
Stop.

---

### COMPLETE

Output: "Feature {feature_id} is already complete. Nothing to resume."
Stop.

---

### ABANDONED

Output: "Feature {feature_id} was abandoned. Check the archive at {archive_dir}/{feature_id}*.md"
Stop.

---

### Unknown status

Output:
  "Feature {feature_id} has an unrecognised status: {status}
   Valid states: CREATED PLANNING AWAITING_CONFIRMATION CONFIRMED IMPLEMENTING
                 REVIEWING REVIEW_PASS REFINING SRS_UPDATE COMPLETE PAUSED ESCALATED BLOCKED ABANDONED
   Correct the frontmatter manually and re-run /resume {feature_id}."
Stop.
