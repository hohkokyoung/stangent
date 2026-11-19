# Confirming D322's checklist on an unseen key: Kavita (frozen before the key is written, 2026-11-15)

1. Key: one fresh Opus 5.5 agent, told only that it writes a benchmark for reviewing an FSD against code (not what is
   tested, nothing about checklists or layer 3), writes `key/fsd.md` (about 15 screens, section ids in brackets) and
   `key/answer-key.json` in the Sonarr key's shape: 30 planted items, 10 gaps, 10 conflicts, 10 undocumented (3 whole
   screens, 7 behaviours on described screens, a natural mix of options, actions and rules), every other sentence true to
   the code; scope as route prefixes. Source only, at the pinned commit 2d225367; never `specs/`. Code then checks: planted
   sentences word for word in their sections, ids unique, kinds 10/10/10; the matching rule is copied from the Sonarr key.
   Nobody edits the key after that. The orchestrator does not change either prompt after seeing it.
2. Two fresh Sonnet 5.5 agents, in parallel, same FSD and scope: (a) alone, D321's prompt word for word with Kavita's
   paths; (b) with `checklist.py`'s list (249 items: 26 screens, 223 clicks) and `check_output.py`, the run-3 prompt word
   for word with Kavita's paths (quoted "covered").
3. Each scored by `--score-fsd` on the frozen key; each one's sample of up to 20 unplanted (seed 42) to its own fresh Opus checker.
4. Reading, frozen: the checklist is confirmed if (b) finds more than (a), its false alarms are no higher than (a)'s
   range's top, and its tokens are within 10% of (a)'s (D320). (b) finding as many or fewer: not confirmed, the Sonarr 29
   was tuning, and the checklist goes back to the owner. Both against the old pipeline only in words: it never ran on Kavita.
Limits: one run each; the key is AI-written; Kavita is public.
