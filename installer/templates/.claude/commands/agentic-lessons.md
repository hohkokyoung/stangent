---
description: Distill recurring reviewer findings from past runs into a capped lessons file the planner learns from
argument-hint: ""
---

# /agentic-lessons

Turn accumulated `## Review` findings into durable, project-level lessons. The
mechanical scraping and the capped/deduped file are handled by
`.claude/hooks/lib/lessons.py`; **you** do the distillation — deciding which raw
findings recur and phrasing each as one general rule. `/agentic-plan` injects
the result into the planner so future plans avoid repeat mistakes.

## Procedure

1. **Collect raw findings.** Run:
   ```
   python3 .claude/hooks/lib/lessons.py collect
   ```
   This prints a JSON array of `{run_id, task_id, review}` for every task whose
   `## Review` section is non-empty. If the array is empty, print "No reviews to
   learn from yet." and STOP.

2. **Read the existing lessons** so you don't re-add what's already captured:
   ```
   python3 .claude/hooks/lib/lessons.py show
   ```

3. **Distill.** Across all collected reviews, identify findings that are
   **recurring or systemic** — the same class of mistake showing up in ≥2 tasks
   or runs (e.g. "missing server-side validation on user input", "timestamps
   stored in local time"). Ignore one-off, task-specific notes. For each
   distinct recurring lesson that is not already present:
   - Phrase it as a single, general, actionable rule (not a reference to a
     specific task or file). Good: "Validate and constrain all user-supplied
     string lengths server-side." Bad: "t3 forgot to validate the bio field."
   - Keep it to one line.
   - Add it:
     ```
     python3 .claude/hooks/lib/lessons.py add "<the distilled lesson>"
     ```
   The script deduplicates and caps the file at 30 entries (oldest dropped), so
   you do not need to prune manually.

4. **Report.** Print the number of lessons added this run and the current
   contents:
   ```
   python3 .claude/hooks/lib/lessons.py show
   ```

## Constraints

- Do NOT add task-specific or file-specific notes — only generalizable rules.
- Do NOT edit `.claude/state/lessons.md` by hand; go through `lessons.py add`
  so the dedup and cap are enforced.
- Do NOT modify any task file or run any other agent. This command only reads
  reviews and writes the lessons file.
