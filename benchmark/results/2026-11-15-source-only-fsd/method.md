# Source-only agent on the planted Sonarr FSD (D321). Frozen before the run, 2026-11-15.

Question: does an agent with only the Sonarr source find the planted FSD's mismatches as well as the old linking
pipeline (2a reference pages + 2b/2c links + D119 disprove + D151 page-gap-strict), which found 24 of 30 with 31.4% false alarms?

- Input: `input/fsd.md` (the frozen outline `benchmark/fsd/sonarr/sonarr-fsd.outline.md`, headings given their section ids
  the way the importer makes them; the sketch image replaced by a note) and the clone `~/stangent-bench/sonarr` at the pinned
  commit 0bffefe7. Scope: the key's route prefixes (the old pipeline had them in `stangent.yaml`). Not allowed: `specs/`,
  `.stangent/`, `stangent.yaml` in the clone, anything outside the clone but `fsd.md`, the web.
- Agent: one fresh agent, Sonnet 5.5, the model the old pipeline's judges used, so the difference is the layers, not the model.
- Output: `findings.json`; `to_trace.py` turns it into a `specs/trace.yaml` in a scratch root; the existing scorer
  (`Stangent.Benchmark --score-fsd`, FsdScore, the key's frozen matching rule) scores it. No rule changed.
- False alarms: the scorer's sample of up to 20 unplanted findings (seed 42) goes to a fresh Opus 5.5 checker with the
  source, verdict real or false with evidence, the same format as `benchmark/results/2026-11-07-phase2c-d151-handcheck.json`.
- Recorded: found (lenient and as kind, by kind), false-alarm share, the agent's tokens; beside the old pipeline's
  24 of 30 (gap 10, conflict 10, undocumented 4), as kind 20, false alarms 31.4%, and its spend.
- Reading, frozen: source agent finds fewer than 24 or has more than 31.4% false alarms → the old layers earn their place
  on this job. Finds 27 or more with false alarms at or below 31.4% → the pipeline's design is rethought before layer 5.
  Between (24–26 found, ≤31.4%) → a tie or better for the source agent, neither at 2c's bar; reported as such.
- Limits: one run; the FSD and key were written by an AI in this project; Sonarr is public, so the model may know it.
