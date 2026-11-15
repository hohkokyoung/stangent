---
description: Review an imported FSD against the code (layer 5, D341) and list its gaps, conflicts and what it leaves out.
argument-hint: "<doc id>"
allowed-tools: Bash(stangent fsd:*), Bash(*fsd-review-run.sh:*)
---

Review the agreed FSD against the source with stangent (D341). A reviewer agent reads the FSD and the code, two runs side
by side; code checks every finding (its section, its FSD quote word for word, every `file:line`) and merges the runs. Never
judge a finding yourself, and never edit `specs/fsd/<doc>/review.json` or `review.md`: they're generated.

**The reviewer runs through the batch driver (D161),** never as a subagent in this session: the driver gives it the model
and effort pinned in `stangent.yaml` and records them. If it stops with `ai_settings_missing`, tell the person to set
`ai: { model: …, effort: … }` in `stangent.yaml` (or pass `--model` and `--effort` for one run) and stop: don't choose
either yourself.

It costs money: each run is capped by `fsd.reviewBudget` (60 turns, $3 by default) and there are `fsd.reviewRuns` of them
(2). Say so and ask before running it.

```bash
${CLAUDE_PLUGIN_ROOT}/scripts/fsd-review-run.sh --max-cost "$STANGENT_MAX_COST" $ARGUMENTS
```

`--again` reviews again although the FSD and the commit are unchanged (otherwise checked runs are kept).

Then report from `specs/fsd/<doc>/review.md`: the counts by kind, how many every run found, and anything dropped by the
code checks with its reason. Say plainly that each finding rests on an AI's reading of the code: code checked the quotes
and lines, not that the difference is real. The old route, `/stangent:fsd-link`, still runs until layer 7 retires it.
