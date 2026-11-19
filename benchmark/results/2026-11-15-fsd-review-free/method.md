# `stangent fsd review` checked for free on the saved Kavita runs (D341 (5), 2026-11-15)

No AI call. The saved source-only runs `2026-11-15-kavita-fsd/alone/` and `alone2/` (D323, D324: 28 and 28 of 30 alone)
went through the built command as if the reviewer had returned them:

1. A shared clone of `~/stangent-bench/kavita` at the pinned 2d225367, `stangent init`, scope as the runs' prompt.
2. The FSD the runs read (`fsd.md`, sha256 e92ec11e…, as `key-frozen.sha256`) turned into `fsd-outline.md` (headings one
   level up, the `[section id: …]` brackets dropped, the title as `%`), written to Word by `Stangent.Benchmark --fsd-docx`
   and imported as `kavita-fsd`: 30 sections, ids identical to the brackets.
3. `fsd review kavita-fsd --run 1|2`, each saved `findings.json` copied to the run's `output.json`, `fsd review-check`,
   `fsd review-merge`.
4. `review_to_trace.py review.json <root>`, then `Stangent.Benchmark --score-fsd <root> <frozen key>` (sha256 bebf2038…,
   checked). The key was not read.

Results (`score.json`): 29 of 30 (gap 10, conflict 10, undocumented 9; missed U9), 29 as their kind; 51 findings, 22
unplanted (worst case 43%, not hand-checked: D323's check on run 1 found 2.7% false). Checks: all 37 + 43 findings passed
first time, 0 dropped, no retry. Merge: 20 gap/conflict pairs joined on the same quote (all 20 the same difference by
reading), 9 undocumented pairs joined, 13 findings marked `maybeSameAs`, 29 found by both runs.
`score-loose-join.json`: D341's first join rule ("same section and an evidence file in common", one per run) gives 47
findings and the same 29, but 3 of its 10 file joins paired different differences (D341 amended).
Limits: the checks were run on output that never saw them; a live run's retry is untested; one key, AI-written.
Re-run after the fresh check's fixes (quote rule, stage on the state module, review-status; D341 (a)–(d)): identical
counts. `review.json`, `review.md`, `score.json` and `run-*-checked.json` here are from that re-run.
