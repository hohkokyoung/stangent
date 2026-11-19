# Is the source-only agent stable run to run? (D324, frozen before the runs, 2026-11-15)

Why: the owner's pivot (a code-made business document from the graph, flows and pages, linked to the FSD, read by a PM)
can't win on accuracy (the agent reading source beat the page route: Sonarr 26 vs 24, Kavita 28). Its only claim is a
stable, complete answer every time. That matters only if the agent is not stable.
Runs: two more fresh Sonnet 5.5 agents with the Kavita "alone" prompt word for word (`alone/prompt.md`), only the output
path changed (`alone2/`, `alone3/`), in parallel; same frozen key (sha256 checked), same FSD copy. Run 1 = `alone/` (28 of 30,
missed U5, U9; 37 findings; 205K).
Compared by code: planted items found per run; "flicker" = planted items found by some runs but not all; findings per run;
tokens. No false-alarm checks (the planted items answer the question exactly).
Reading, frozen:
- Stable: every run finds at least 27, and flicker is at most 2 → one run is enough for a PM; the business document has
  no job on stability grounds.
- Unstable: any run finds 26 or fewer, or flicker is 3 or more → a single run can't be trusted to be complete; a stable,
  code-made base document has a job.
- Otherwise: reported as found, and talked through.
Limits: three runs; one key, AI-written; Kavita is public.
