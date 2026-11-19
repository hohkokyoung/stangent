# Does the truth map judge tickets right? (D358, redone after D361–D374)

**Frozen 2026-11-17, before the planter runs.** Nothing below changes after the planter's output is written. The plan
it replaces (2026-11-16, never run) asked whether the FSD's text helps the reproducer; D361 moved the judge from the FSD
to the truth map, so that question is gone (history at the end).

## The question

With the PM's rulings in place (`rulings.yaml`, `truth-verdict` on), does stangent:

- **(a)** call each bug ticket **backed**, **contradicted** or **unclear** as the rulings say;
- **(b)** never write or run a test for a ticket the rulings contradict or don't settle, so it never reaches a fix
  (`fix start` refuses any proof that isn't `reproduced`, D346);
- **(c)** after a `Ruling:` reply in Jira, draft the ruling (layer 5, code), and once a person commits it, prove the
  ticket again to the verdict the reply means?

## What is already set up (free, done)

- The Kavita clone `~/stangent-bench/kavita` at the pin `2d225367f0`: graph, flows and inventory built (code-graph v7);
  the FSD imported as `kavita-fsd` (from `benchmark/results/2026-11-15-fsd-review-free/fsd-outline.md`); the saved review
  turned into `specs/fsd/kavita-fsd/rulings.yaml`: 42 entries in 19 sections and the code no section covers, all blank, no
  matches (the saved review predates matches, so no match is measured here); `truth-map.md` numbers them 1–42.
- `components: truth-verdict: true` in that clone's `stangent.yaml` only (the default stays off).
- The clone stays local: its commits and branches are never pushed.

## The planter (one fresh Opus agent, blind for this session)

It reads what this session never reads: Kavita's planted FSD and its key (`benchmark/fsd/kavita/`), with the clone's
`rulings.yaml`, `truth-map.md`, `review.json` and the code. It writes everything below, and checks each claim by running
commands, recording their output.

1. **The rulings (the "planted rules").** It answers the truth map as a PM would, only through
   `stangent fsd rule kavita-fsd <n> <answer> [--words "…"]` (never editing YAML), then commits `rulings.yaml` as
   `Planter PM` and runs `stangent fsd rulings-check kavita-fsd`. A ruling may follow the FSD key or go against it (a PM
   decides); its key records which. It leaves blank exactly the 2–3 entries its key names, for unclear tickets.
2. **The tickets: 9, three of each kind,** each a Kavita bug report as a real reporter would write it (current behaviour,
   expected behaviour, steps), never quoting the FSD, a ruling or the truth map:
   - **backed (3):** the reporter expects what an `fsd-right` or restated ruling says, and the code at the pin does
     otherwise. Nothing is planted in the code: the ruling says the code is wrong. Each must be checkable by a unit test in
     an existing test project (`Kavita.Services.Tests` or `API.Tests`); the planter shows it with a throwaway test that
     fails at the pin, kept sealed, never given to the reproducer.
   - **contradicted (3):** the reporter expects the opposite of a `code-right` or `keep` ruling.
   - **unclear (3):** at least one on a blank entry and at least one on code no entry covers ("not covered").
   Keys `KAVR-1` … `KAVR-9`, the kinds shuffled, in the replay layout (`<KEY>/raw/getJiraIssue.json`, the shape
   `tests/Stangent.Core.Tests/TicketSupport.cs` writes) under `public/tickets/`.
3. **The replies (2), for (c):** for one unclear ticket on a blank entry, the PM's reply `Ruling: the FSD is right` or
   `Ruling: the code is right`; for the not-covered one, a reply with the rule in the PM's own words. Each with the verdict
   it should lead to.
4. **The key** (`sealed/key.json`, read only by scoring code): per ticket its kind, the entry it rests on (`n` and key, or
   none), the file that entry's evidence is in, and the expected verdict; per reply the expected verdict after it; the
   rulings it made and which of them go against the FSD key; the throwaway tests' output. Hashes in `sealed/SHA256SUMS`.

## The runs (all through `plugin/scripts/ticket-run.sh`, `--max-cost` set, from the pin, `tickets.replay: public/tickets`)

1. **Every ticket:** `ticket-run.sh --max-cost <cap> --prove KAVR-n`: pull (replay) → points (the file finder, D101) →
   bundle (its `truth` list) → prove (the reproducer gives its verdict in the same call, checked by code; no AI call when
   nothing usable covers the ticket).
2. **The reply tickets:** `ticket draft --kind questions` (the questions writer; code appends the PM's question). Replay
   can't post, so `reply.py` (a small script, written before the planter runs) does what Jira would: records the draft as
   posted with a comment id, and adds to the replay ticket the question's comment and then the planter's reply. Then
   `ticket pull` (code: drafts the ruling, uncommitted), a commit as `Planter PM`, `fsd rulings-check`, and
   `ticket-run.sh --no-pull --prove` again.

## What's measured (by code, `score.py`, written before the planter runs)

- **Verdict right:** the latest attempt's `verdict.verdict` equals the key's. Counted over all 9, and over the tickets
  whose bundle `truth` list holds the key's entry (the file finder found its file), so a finder miss is told apart from a
  wrong verdict. A finder miss on a backed ticket shows as unclear ("not covered"): counted as a miss, reported apart.
- **No test for a wrong ticket:** for each contradicted and unclear ticket, `outcome` is `not-provable` and no test was
  run (`bug-proof.json` records none), and `stangent fix start` refuses it.
- **Backed tickets:** `outcome` `reproduced` (the test fails at the pin as the ticket says).
- **(c):** pull drafts the reply's ruling on the key's entry (or a `ticket:KAVR-n` entry for not covered), the bundle
  lists it unusable before the commit and usable after, and the second proof's verdict equals the key's.
- Also recorded: tokens and dollars per step, retries, and which entry each verdict cited.

## The bar (frozen)

- **The truth map works** if: no contradicted or unclear ticket gets a test run (0 of 6: one is a fail, since stopping
  a wrong ticket is what the truth map is for); the verdict is right on at least 8 of the 9, or on every ticket whose
  entry reached the bundle; at least 2 of the 3 backed tickets reproduce; and both replies lead to the right verdict.
- **It doesn't** if a contradicted or unclear ticket gets a test run, or the verdict is right on 6 or fewer of the 9 with
  the entry in the bundle.
- Anything between is talked through with the owner, not called either way. Finder misses are reported as their own
  number: if they decide the result, the next work is linking tickets to entries, not the verdict.

Small on purpose: 9 tickets on one repo and an AI-written FSD. It can show the truth map stopping wrong tickets, or not;
nothing finer.

## Cost (asked before each paid step)

- **The planter:** one Opus agent, about 250–400K tokens (more than the earlier 200–300K: 42 rulings, 9 tickets, 2 replies,
  3 throwaway tests), about $4–8.
- **The tickets:** finder up to $1 each (`findBudget`), proof up to $1 each (`proveBudget`), the questions writer a few
  cents; unclear tickets with nothing usable make no proof call. 9 tickets + 2 second proofs: about $5–12 typical, $20 at
  most, under `--max-cost 20`.
- **In all:** about $10–20. No fix runs: (b) is scored by `fix start` refusing, which is free.
- Headless `claude -p` runs wait until 2026-11-18 (the limit noted for D341's live run).

## History

The 2026-11-16 plan, superseded before any bug was planted (D361–D364): it asked whether a rule stated only in the FSD
reaches the reproducer's test and then the fixer, with planted code bugs, an arm with the FSD sections and one without.
Two things ended it: `TicketBundle.Markdown` printed sections' titles, not their text (fixed in layer 4, D372), and the
owner moved the judge to the truth map. `flow-anchor/` holds the free checks behind D361 and D362.

## Adapted 2026-11-18 (D406), before the planter runs

The frozen plan was written for the saved review's 42 blank entries with no matches. It now runs on the truth map
built from D404's live review (`specs/fsd/kavita-fsd/` on the clone's `bench/fsd-migrated`, commit `1d58caa`): **117
entries** (gap 11, conflict 9, match 26, undocumented 71). The 26 matches already carry Claude's blind test rulings
(D405: 23 confirmed by section, entries 9, 54 and 73 answered no); the planter must not change them. Everything else
stands, with these changes:

- **The rulings:** the planter rules every blank entry (the 20 differences and the 71 undocumented) except the 2–3 its
  key leaves blank for unclear tickets. Many undocumented entries can share one answer (`keep`), but each is answered.
- **The tickets (still 9, 3 of each kind), two of them on matches:** one **contradicted** ticket expects the opposite
  of what a **confirmed match**'s FSD sentence says (the code does what the FSD says; the reporter wants something else);
  one **unclear** ticket rests on a **not-a-match** entry (9, 54 or 73: the PM said the code doesn't do what the FSD says,
  and no rule says what it should do).
- **The clone:** no longer at the bare pin: `bench/fsd-migrated` adds only `specs/` and FSD commits on top of
  `2d225367f0`, so the code is the pin's. `tickets.replay` points at this folder's `public/tickets` (absolute path).
- **The key** (`sealed/key.json`, fixed shape so `score.py` can read it):
  `{"tickets":[{"key","kind","entry":{"doc","n"}|null,"evidenceFile","expected"}],
  "replies":[{"key","text","expectedAfter"}],"rulings":[{"n","answer","againstFsdKey"}],"tests":[{"key","output"}]}`.
- **The replies:** `reply.py` and `score.py` (this folder) are written before the planter runs. Before the second proof
  of a reply ticket, the run copies its `bundle.json` to `bundle.before-reply.json`, so "unusable before, usable after"
  can be scored.
- **Cost:** the planter answers 91 entries, not 42: about 300–500K Opus tokens. The tickets keep `--max-cost 20`.
