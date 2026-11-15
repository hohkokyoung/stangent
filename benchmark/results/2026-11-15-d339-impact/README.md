# D339: does `graph impact` help an agent name the whole fix? (Sonarr's 40 bugs)

Frozen 2026-11-15, before any paid run. Reading and scoring: `decisions.md` D339 and `benchmark/tickets/score_d339.py`.

## Set-up
- **Graphs** at each bug's fix parent: `benchmark/tickets/parent_graphs.sh ~/stangent-bench/sonarr benchmark/tickets/sonarr ~/stangent-bench/stangent-bin-d339/stangent.dll ~/stangent-bench/sonarr-parents`
  (stangent built from this worktree at `df7f57b` plus D337–D339's docs; frozen copy in `~/stangent-bench/stangent-bin-d339`). About 55 s each, no AI. Log: `~/stangent-bench/sonarr-parents/builds.jsonl`, copied here as `builds.jsonl`.
- **Side A** (the 2026-11-02 baseline, run again fresh):
  `STANGENT_MAX_COST=8 python3 -I ../../tickets/agent_baseline.py ~/stangent-bench/sonarr ../../tickets/sonarr side-A.json SONB-1 … SONB-40`
- **Side B** (the same plus `stangent graph impact`, prompt + `IMPACT_LINE`):
  `STANGENT_MAX_COST=8 IMPACT_GRAPHS=$HOME/stangent-bench/sonarr-parents STANGENT_DLL=$HOME/stangent-bench/stangent-bin-d339/stangent.dll python3 -I ../../tickets/agent_baseline.py ~/stangent-bench/sonarr ../../tickets/sonarr side-B.json SONB-1 … SONB-40`
- **Score:** `python3 -I benchmark/tickets/score_d339.py ~/stangent-bench/sonarr side-A.json side-B.json --old benchmark/results/2026-11-02-agent-baseline-{pilot,sonarr-rest}.json`

## Before the run (free)
- The scorer on the saved 2026-11-02 baseline: any fix file 36 of 40; mean share of the fix named 76% (39 scored; SONB-32's files are all created by the fix); **multi-file fixes (21, 20 scored): mean share 58.2%, whole fix named 5, wrong files 2.43 a ticket, named 4.1.** (D339's "52%" was over all fix files, created ones included; the share here counts only files that exist at the parent, as D339 defines it.) So side B passes by share at 63.2% or more on multi-file fixes, if side A reruns the same.
- Smoke test of side B (SONB-34): not run. Headless Claude Code answered "You've hit your weekly limit · resets Nov 18 at 12am (Asia/Singapore)". Nothing was spent.

## Graphs (2026-11-15, free)
All 40 built at their parents (`builds.jsonl`; commit = parent on every one; 35–41K points; history full), 34 minutes in all.
- SONB-23 (issue 8187): first build had one load error, a crashed restore (exit 139) of `Sonarr.Mono.Test`; rebuilt once, clean (the log keeps both lines).
- The five oldest parents (SDK 6.0.405: issues 7518, 7527, 7555, 7589, 7608) built without frontend packages (`npx install failed: ERR_PNPM_BROKEN_LOCKFILE`), so their frontend calls are matched by name (🟡), and the Windows tray project didn't load (no osx runtime pack). Of those, only SONB-36 (7608) has a frontend fix file. Recorded as a limit, not tuned.

## How it ran (D339's amendment)
Headless Claude Code kept answering "weekly limit" (Homebrew 2.1.292 and the desktop app's 2.1.325, every model), so both sides ran as Sonnet subagents (Explore) of the session: exports from `benchmark/tickets/d339_prepare.py` in `~/stangent-bench/d339-exports/`, one prompt per side (the ticket in a file beside the export), only the 21 multi-file fixes. Transcripts: `transcripts/*.jsonl.gz` (gzipped; `runs.json` names the session's temporary copies, gone after the session); rows: `side-A.json`, `side-B.json` (`benchmark/tickets/d339_collect.py runs.json`); score: `score.json` (`score_d339.py`).

## Result: pass, fragile
| Multi-file fixes, 20 scored | Side A (alone) | Side B (+ `graph impact`) |
|---|---|---|
| Mean share of the fix named | 54.2% | **62.3%** (+8.2 points; bar +5) |
| Whole fix named | 3 | **8** |
| Wrong files a bug (20 / 21 bugs) | 2.05 / 2.14 | 2.45 / 2.57 (bar: at most +1) |
| Any fix file named (21) | 19 | 19 |
| Tokens, cache reads included | 5.68M | 6.61M (+16%) |

- B better on 5 bugs (SONB-1, 14, 17, 31, 39), all two-file fixes where impact pointed at the second file; worse on 4 (SONB-2, 3, 6, 38), fixes of 3–9 files. One run each; side A against its 2026-11-02 run moved 4 points (58.2% → 54.2%), so the margin is about twice that noise.
- Isolation: no read outside an export on either side; side A used only Read/Grep/Glob; side B ran plain grep/ls/find through Bash inside its export 27 times (no information side A lacked), which the instruction didn't allow.
- Fresh check (D61, Sonnet, ~75K tokens): every answer matches its transcript; shares, whole-fix counts and isolation recomputed independently; the key confirmed on 3 seeded bugs; its one note was the 21-bug denominator for wrong files, now shown both ways.
