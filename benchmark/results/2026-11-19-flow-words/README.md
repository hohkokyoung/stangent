# D416's frozen measurement: the PM view's flows in plain words (2026-11-19)

The bar, frozen in D416 (4) before anything ran: **at least 95% of judged lines true on each app, and the owner's 10
agree with the judge.**

| | Kavita (`kavita-fsd`) | Sonarr (`sonarr-fsd`) |
|---|---|---|
| Linked flows worded | 8 | 19 |
| Worded lines (failed the code check) | 63 (0) | 131 (0) |
| Judged | all 63 (a census: fewer than 100 exist) | 100, seed 4161 |
| **Run 1** (seed 416), true / false / can't tell | 59 / 4 / 0 = **93.7%: below the bar** | 82 / 18 / 0 = **82%: below the bar** |
| **Run 2** (after the picker fix, seed 4161): **passes** | 63 / 0 / 0 = **100%** | 100 / 0 / 0 = **100%** |
| Wording cost, run 1 + run 2 | $0.066 + $0.059 | $0.087 + $0.096 |

Total paid: **$0.31** for four wording calls (Sonnet 5.5, medium, pinned as `ai.steps.stangent-view-writer` in both
clones' untracked `stangent.yaml`). None needed a retry. The judges were four fresh Sonnet subagents, read-only on the
clones, about 350K tokens in all. The estimate had been $4–6. The real cost is lower because the writer only sees
flows linked to a numbered question: 8 on Kavita and 19 on Sonarr, not hundreds.

**The owner's 10:** drawn before run 2's judges ran (`owner-spot-check.md`, `random.Random(4162)` over the 163 judged
lines). The owner marked all 10 true (2026-11-19, "true"), the same as the judge.

**Result: the bar is met on run 2.** Both apps are at least 95% true (100% each), and the owner's 10 agree with the judge.

## Run 1: what failed, and why

All 22 false lines (Kavita 4, Sonarr 18) shared one cause, and it wasn't the writer. The flow files store each condition
with its kind, and an `unless` is an earlier guard that must be false for the step to run. The picker (`FlowWords.Pick`)
passed on only each condition's code and dropped its kind. So Sonarr's validation stop at `RestController.cs:157` reached
the writer as "when `errors.Any()`, `resource == null`", and the writer faithfully wrote "An empty request is refused
with a list of validation errors". 13 of the 18 Sonarr lines are that one shared stop, repeated across flows.
`useProviderSchema.ts:51` (4 lines) and `SchemaBuilder.cs:262` (1 line) failed the same way. On Kavita, the guards
were `user == null`, `BookReaderHighlightSlots.Any()` and `emailValidationErrors.Any()`. Every line whose conditions
were all plain `if`s was judged true (98 of 98). Of the lines carrying a non-`if` condition, 43 of 65 were true.

**The fix (`FlowWords.Pick`):** each condition now reaches the writer as the flow's own Markdown reads it
(`FlowFiles.ConditionText`, D173/D240), for example "only if `resource == null` is false (an earlier guard)". The
writer's instructions now say that a step's conditions all hold together (two Kavita lines had joined them with "or").
The view's folded developer section shows the same reading. Tests: 2,003 pass, with the refund fixture now holding an
`unless`. The fix changed every flow's pick hash that had a condition, so run 2 re-worded 7 of Kavita's 8 flows and
all 19 of Sonarr's. Run 2 used a new seed, so Sonarr's sample isn't the set of lines that exposed the fault. Run 1
stands as recorded: the bar was missed on the first measurement, and passed on the second.

## Left, not fixed here (both fixed the same day by D420: `null` reads "is null", `truth-map` v3)

- **`null` conditions have no reading in `FlowFiles.ConditionText`:** a `x ?? throw` reads "only if `x`", which is
  the opposite (layer 4's chart says "is null"). This touches 4 Kavita lines, all one step
  (`ClaimsPrincipalExtensions.cs:28`). The writer worded all 4 right from the code, and the judge marked them true. The
  fix belongs in the flows layer: it changes the flow Markdown and layer 4's condition text (D20).
- **`truth-map.json` drops a condition's kind too** (`TruthMap.cs:377`): agents reading a step's conditions see an
  `unless` as its bare code. This is contract `truth-map` v2.

## Files

- `kavita-sample.json`, `sonarr-sample.json`: run 2's lines, each with its flow, pick id, the pick's code, conditions
  and `file:line`, the source around it, and the seed (`benchmark/flow-words-sample.py`; the re-pick matched the
  writer's input on every line).
- `kavita-verdicts.json`, `sonarr-verdicts.json`: run 2's judge, one verdict and reason per line.
- `owner-spot-check.md` / `.json`: the owner's 10, with their code.
- `run1/`: run 1's samples, verdicts, both `flow-words.json` files, and the 10 drawn for it (superseded, never shown).

```bash
python3 benchmark/flow-words-sample.py ~/stangent-bench/sonarr sonarr-fsd out.json --seed 4161
python3 benchmark/flow-words-sample.py spot owner-spot-check.md kavita-sample.json sonarr-sample.json --seed 4162
```
