# First live `stangent fsd review` on Kavita's planted FSD (D341's owed live run, D399, 2026-11-18)

Setup: `~/stangent-bench/kavita` on branch `bench/fsd-migrated` (`f4abc65`: the planted FSD migrated to chapter files;
no earlier review, rulings or truth map in the commit). `plugin/scripts/fsd-review-run.sh --max-cost 8 kavita-fsd`,
reviewer Sonnet 5.5 at medium effort (`ai.steps.stangent-fsd-reviewer`), default `fsd.reviewBudget` (60 turns, $3),
2 runs merged. Claude Code 2.1.292. Scored as the free check: `review_to_trace.py review.json root`, then
`Stangent.Benchmark --score-fsd root benchmark/fsd/kavita/answer-key.json` (sha256 bebf2038…, equal to
`2026-11-15-kavita-fsd/key-frozen.sha256`). The key was not read.

## Result
- **11 of 30** (gap 6/10, conflict 5/10, undocumented 0/10), all 11 as their kind. Free check on the saved runs: 29.
- 20 merged findings: 6 gap, 5 conflict, 9 match, 0 undocumented; run 1 gave 11, run 2 gave 18; 0 dropped, no retry.
- Cost $0.40 in all. Run 1: 21 turns, 37 s, 4.6K output tokens; run 2: 38 turns, 69 s. Both ended on their own,
  far under the cap (the saved runs used 205–242K tokens each). Tools: run 1 15 Grep, 2 Glob, 2 Read.

## How this run differs from the measured ones (D321–D324)
1. **No scope:** the clone's `fsd.scope` is `[]`, so the input says "The FSD covers every screen of the app". The
   measured prompt named nine route prefixes. Undocumented whole screens (kind (a)) are judged against that scope.
2. **A fourth kind, `match` (D366, the truth map's layer 0):** the agent is asked for every statement the code backs
   as well. 9 of 20 findings are matches. Never measured live before this run.
3. **Generic prompt:** no app name or description; the FSD inline, not a file; structured output, not a file write.
4. **Headless `claude -p` at medium effort** vs agents spawned by an orchestrating session (effort not recorded).

Which of these costs the 18 items isn't known from one run. Files: `review.json`, `review.md`, `score.json`, `root/`.

## Reruns, one change at a time (owner: "Yes, up to $3"; $2.42 spent)
Per-run cap lowered in the clone to fit ($1, then $0.35; the guard reserves a full cap per parallel run); turns 60.
Scores are per run, then merged where both runs finished.

| Run | Change | Run 1 | Run 2 | Merged | Cost |
|---|---|---|---|---|---|
| first | none (no scope) | — | — | 11 (gap 6, conflict 5, undoc 0) | $0.40 |
| A | `fsd.scope` = the measured nine prefixes | 13 (51 turns) | 4 (16 turns) | 13 | $0.43 |
| B | A + effort high | 17 (49 findings, 32 of them matches; hit its $1 cap after one retry) | cut by `--max-cost` | — | $1.78 |
| C | A, prompt without `match` (`C-nomatch/fsd-reviewer-nomatch.md`, built from a scratch copy) | 2 | 4 | 5 | $0.21 |

Measured single runs (D323, D324, prompt D321, no match kind): 28 and 28.

**Reading:** the reviewer stops by its own choice, well under its turn and dollar caps. Run C's agent wrote "Two
conflicts confirmed so far. I'll write up the findings." and answered. Taking out `match` didn't help (C is the
lowest); high effort helped some (17) at about 5× the cost and spent most of its output on matches. The setting
that differs from every measured run is the harness: headless `claude -p` with Claude Code's own system prompt and
structured output, against agents an interactive session started, told to write a file and reply with counts.
Nothing in the live call makes the agent cover every section or every screen in scope. Limits: one or two runs
each, high variance (4 to 13 on the same setup).

## D400's live rerun: coverage required (owner: "Yes, max $3"; $0.60 spent)
`D-coverage/`: the scope as A, medium effort, per-run cap $1.50 then $1.30 (the guard refused both retries at $1.50).
First attempts: run 1 listed 23 sections and all 22 screens with 6 findings in 17 turns; run 2 listed 12 sections
with 2 findings in 5 turns. Both went to the retry for missing coverage, then were recorded.

| Run | Score | Findings | Uncovered after the retry | Cost |
|---|---|---|---|---|
| 1 | 4 (conflict 2, undoc 2) | 9 | sections 9.2, 10 | $0.20 |
| 2 | 5 (conflict 2, undoc 3) | 8 | none | $0.16 |
| merged | **6 of 30** (gap 0, conflict 3, undoc 3) | 12 | | $0.36 + $0.24 |

**Reading:** worse than A (13) on the same setup. The agent meets the coverage floor by citing a line per section and
naming a section per screen, without checking them, and finds less, so `review.md` now says "every section checked
against code" beside a 5 of 30 run: the coverage line misleads. Code's screen list gave 2–3 undocumented finds (A: 2).

## Diagnostic: the measured prompt through the same headless harness (owner: "Yes, run it"; $0.84)
`E-measured-prompt/`: D323's `alone/prompt.md` word for word but its three paths (the FSD as a file, holding exactly
the text the command gives; a clean worktree of `f4abc65`; the output file), `claude -p` with the command's flags
(Sonnet 5.5 medium, Read/Grep/Glob, `specs/` denied, 60 turns, $1 cap, `dontAsk`), **no `--json-schema`**, Write allowed
for the output. The Write rule was spelt `Write(...)` and refused (Claude Code's rule for writes is `Edit(...)`), so the
findings were taken from the refused Write call's content, unchanged. 104 turns, 268 s, $0.84.

**18 of 30** (gap 8/10, conflict 8/10, undocumented 2/10), 20 findings, 2 unplanted. The command's runs on the same
harness: 2–17 per run, 3–32 unplanted. So the harness isn't the cause; the command's prompt or its answer format is.
What differs: the command answers through Claude Code's structured output (`--json-schema`, a `StructuredOutput` tool
that ends the run) where this run wrote a file and replied with counts; the command's prompt has the FSD inline, no app
name, the `match` kind (C showed `match` alone isn't it). Undocumented stays low (2) either way; the measured runs got 9.

## Diagnostic 2: the command's own prompt, answered by writing a file (owner: "Yes, run it"; $0.27)
`F-command-prompt-file/`: A's input exactly (built from HEAD `4341dfe`, before D400: scope set, no coverage), with only
the answer line changed from "as your structured output" to "to the file …" and "reply with only the number of findings
by kind" added; the driver's flags and system-prompt line, no `--json-schema`, writes allowed to the output folder only.
**9 of 30** (gap 2, conflict 5, undocumented 2), 15 findings (6 matches), 6 unplanted; 29 turns, 67 s.

**Reading (one run each, and A's two runs ranged 4–13, so this is a lean, not proof):** the answer format isn't the main
cause (F 9 with a file, A 4–13 with structured output); the measured prompt is (E 18, 104 turns, same harness). What the
command's prompt has that D323's doesn't: every statement the code does as written (`match`) asked for in the first
line, the FSD inline instead of a file, no app name, and the "code checks … then it is dropped" paragraph.

## D401: the measured prompt inside the command (owner: "sure"; $1.39)
`G-measured-in-command/`: the built command after D401 (D323's prompt, the FSD as a file, the answer written to a file,
no `--json-schema`), two runs, Sonnet 5.5 medium, $0.85 cap each. Both first attempts worked 74 and 77 turns ($0.55 each)
and tried to write their answer; the driver's read-only hook (T.8, `read-only-hook.sh`) refused the write, the retries
too, and both runs failed. The answers were taken from the refused Write calls, unchanged, and scored:

| Run | Score | Gap | Conflict | Undoc | Findings | Unplanted |
|---|---|---|---|---|---|---|
| 1 | **17** | 6 | 9 | 2 | 17 | 0 |
| 2 | **18** | 8 | 8 | 2 | 20 | 2 |
| both (union, not the merge) | **20** | 8 | 10 | 2 | 37 | — |

Before D401 the same command scored 2–17 per run with 3–32 unplanted. Gaps and conflicts are back to the measured level
(18 of 20, measured 20); **undocumented stays at 2 of 10** (measured 9), the one gap left. Fix for the hook: it allows
the one folder `STANGENT_WRITE_DIR` names, which the driver sets to the throwaway answer folder for the reviewer's call
only; checked for real with one tiny Haiku call each way (named: written; not named: refused), cents.
