# stangent — postmortem

Archived August 2026, after 190 commits. What follows is the part worth keeping:
lessons that are not specific to this repo. The code is not the asset.

## Why it ended

The founding bet was that you could orchestrate specialist agents by writing
careful prose and enforcing the parts that mattered with hooks. Two things
invalidated it. The bet didn't hold — prose instructions get skipped, and there
was no test that could fail when one was wrong. And the harness kept absorbing
the feature set underneath it (subagents, skills, hooks, plan mode, per-role
models), so maintenance was being paid on a shrinking delta.

62 of 190 commits were `fix:`. A third of all work was repair.

## Lessons

1. **Prose is not a runtime.** An instruction with no mechanism behind it gets
   skipped. Marking it "mandatory — do not skip" does not change this. Two tasks
   in one run ignored a call labelled "exactly once — this is not optional".

2. **If a prompt can be wrong and nothing fails, you are searching without a
   gradient.** `planner.md` was rewritten 41 times. Every defect was found in
   production at full token cost and fixed by adding a paragraph — which grew the
   prompt, which raised the cost of the next run.

3. **Don't coordinate a state machine by asking the model nicely.** Logging,
   write-scoping, cost attribution and the edit guard all depended on the
   orchestrator remembering to write three state files and clear them after.
   Every downstream guard then had to be written to tolerate that not happening,
   which hid the drift instead of surfacing it.

4. **Cost is dominated by context re-reads, not call count.** Sum the *result*
   bytes of tool calls. One task did 34 edits for $3.68 while another did 89 for
   $12.28 — because the second echoed a 25 KB file back into context on every
   edit, where every later turn re-read it. Call counting cannot see that.

5. **N sequential edits to one file cost on the order of N².** Script mechanical
   changes — one codemod run, verify, read the diff. Three runaway
   site-by-site migrations cost $31.71 between them.

6. **Advisory guards do not guard.** There were three cost controls. All three
   were non-blocking, on the reasoning that a long task can be legitimate. None
   of them ever stopped a runaway. At least one gate needs teeth.

7. **Model IDs fail silently, so routing moves down but never up.** An
   unrecognised ID does not error — it falls back to the session model. Every
   role on one project ran the wrong model for weeks and cost telemetry stayed
   correct, because it prices what the transcript reports. Validate model config
   against a known list at startup.

8. **Never cost-optimize a gate.** Reviewing looks mechanical from outside, so
   it is the first thing anyone puts on a cheap model. A cheap reviewer's failure
   mode is not missing things — it is reporting an item as *cleared*, which reads
   as "checked and fine" and stops anyone looking again. A Haiku design-critic
   read a comment saying a colour pair fails the 4.5:1 floor and cleared the
   contrast item twice. Its report was also longer, so length is no signal of
   thoroughness. A missed finding is recoverable; a false clear is not.

9. **Pick one way to find the repo root.** This one had three
   (`Path.cwd()`, `parents[2]`, `parents[3]`). The cwd-based bug was fixed twice
   in the hooks and left standing in the nine modules the hooks call.

10. **Docs that double as a lab notebook stop being docs.** The README reached
    65 KB across 54 revisions, carrying live investigation notes. Nobody,
    including the author, could tell what was current.

11. **A well-tested deterministic core wrapped in an untestable prose
    orchestrator fails in the orchestrator.** That is where the tests aren't and
    where there is no cost ceiling. Shrinking that layer was the whole fix.

## Carrying forward

Lessons 4, 5, 7 and 8 were paid for in measured dollars and apply to any agent on
any framework. They are the reason this file exists.
