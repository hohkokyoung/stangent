# D322 first step: the source agent with a checklist and code checks (frozen before the run, 2026-11-15)

Side-by-side under D320 on the planted Sonarr key. "Alone" is D321's run (26 of 30, gap 10, conflict 10, undocumented 6;
7.8% false alarms, range 0-15.6%; 105K tokens), reused, not re-run.
"With": one fresh Sonnet 5.5 agent, D321's prompt word for word, plus:
- a checklist made by code (`benchmark/fsd-agent/checklist.py`): every screen (31) and click flow (116) in layer 3's
  index for Sonarr, grouped by area, with its name and start `file:line`; the rule is fixed and not tuned to the key;
- each item answered: covered (FSD section), left-out (backed by an undocumented finding listing it), or not-for-fsd (reason);
- code checks it must run and pass before it finishes (`benchmark/fsd-agent/check_output.py`): every item answered once,
  every left-out backed, every fsdQuote word for word in the FSD, every evidence path real (line within the file).
Scored like D321: `to_trace.py`, `--score-fsd`, the scorer's sample of up to 20 unplanted (seed 42) to a fresh Opus 5.5 checker.
Reading (D322, D320 as written): the checklist stays if "with" finds more (overall above 26) and its false alarms stay
inside D321's range (at most 15.6%), and its tokens are within 10% of 105K; finding more for more tokens fails D320 as
written and goes to the owner as a question about D320's token rule, not decided here. One run each; same limits as D321.
