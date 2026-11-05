# Decisions D50–D68: reference pages and the FSD

Full entries, word for word. One line each in [`decisions.md`](../decisions.md).

## D50 · stangent is a standalone rebuild, modelled on the company's pipelines — 2026-10-19
stangent depends on nothing the company runs. The company's pipelines are the **model to match**, not a system to
plug into:
- **Spec from code:** a static call graph plus E2E tests produce a cited spec per screen, reconciled each release.
- **Planning → review:** a separate pipeline from plan to reviewed change.
Phase 2 aims to do what the first one does, on stangent's own graph (Phases 1a–1c); Phases 4 and 6 aim at the second.
Where Phase 2 differs from the model, the decision says why.
- *Corrected 2026-10-19:* first recorded as "stangent is the layer under the company's pipelines"; the owner
  clarified that it's a rebuild that only borrows their shape.
**Why:** the company's pipeline shows the shape works in practice; copying a proven shape beats inventing one.
**Cost:** only a one-line description of the model is known here, so details (what "cited" and "reconciled" mean
in practice) are decided in this grilling, not copied.

## D51 · The spec is built from the agreed FSD and the code together — 2026-10-19
- The **functional specification document (FSD)** agreed with the PM and the client and the **code** are always read
  together, each requirement labelled with D13's provenance: both agree, documented only, inferred from code only, or
  conflict. Code isn't a fallback: it adds what the FSD never mentions. With no FSD, the spec comes from code alone
  (the model's way, D50).
- On a conflict the FSD is what was agreed, but stangent still doesn't pick a winner: BA/PM do (D15).
- The code graph and everything else (E2E tests, later tickets) are compared with the FSD to find inconsistencies:
  the discrepancy queue (D14).
- This is where stangent departs from the model (D50): a spec drafted from code copies the code's bugs, so it can't
  show where the code drifted from what the client agreed.
**Why:** what the client signed off on is what the system owes them; code is evidence of what was built, not of what was asked.
**Cost:** document import (D13, D15, D16) stays in Phase 2's scope, and judging "does this code do what the FSD says" is
the step code can't fully check (D18).

## D52 · Two documents, linked: the FSD and a reference generated from code — 2026-10-19
Borrowed from the company's pipeline (D50). Replaces D13's single merged spec.
| Layer | What it is | Who writes it | Checked by |
|---|---|---|---|
| **FSD** | the agreed documents, converted to text deterministically; **never rewritten by AI**; sections keep their own IDs | the client / BA, outside stangent | conversion is code; same file → same text |
| **Reference** | pages describing what the code does, generated from the graph; every statement cites symbol IDs | AI proposes | code: every cited symbol exists in the graph (D18); recorded with its inputs' hashes and the commit it was built from |
| **Links + decisions** | FSD section ↔ reference page, and every gap, conflict and decision | BA/PM and devs | the text hash on both ends (D11): editing either side drops a link back to unverified |
- The link states are D13's labels: **agree**; **gap** (FSD says it, code doesn't); **undocumented** (code does it, FSD
  doesn't, which the company's pipeline doesn't list); **conflict** (they disagree: BA decides which is right; if the code
  is wrong, it goes to PM/dev as a ticket).
- The reference is committed: it's AI output, which isn't reproducible, so it's stored rather than regenerated (D18).
  The graph it cites is not (D25).
- The chain FSD section → reference page → code symbols lets impact answers reach FSD sections (D10, D27).
Rejected: one merged spec (D13 as first written): an LLM rewriting the FSD into requirements is where hallucinations get in,
and it mixes "what was agreed" with "what was built".
**Why:** less AI (nothing rewrites the client's words), each layer has its own check, and the only layer people write is small.
**Cost:** two documents to read instead of one; an FSD section and a page rarely match one to one, so links are many-to-many.
- *Revised 2026-11-02 (D99, not built):* the "Rejected: one merged spec" reason is answered rather than dropped: a
  structured spec is built on top of the FSD, which stays the record byte for byte; every item quotes it word for word
  (checked by code), a fresh agent checks each item says no more than its quotes, and the BA approves each one. The FSD,
  the reference and the links stay separate layers.

## D53 · Links: code finds candidates, AI judges with quotes, code checks, a person approves — 2026-10-19
How an FSD section ↔ reference page link and its state (D52) are decided:
1. **Code proposes candidate pairs** where names match: the FSD's "Checkout screen" ↔ the `/checkout` route, an FSD
   field label ↔ a DTO property, a screen name ↔ a component.
2. **AI judges each pair** (agree / gap / conflict / undocumented), quoting the exact FSD sentence and the exact
   reference sentence it relies on.
3. **Code checks** both quotes exist word for word, both IDs exist, and every symbol the quoted reference sentence cites
   is in the graph. A failed check is rejected before anyone sees it.
4. **A person approves.** "Agree" links are approved in bulk per FSD chapter; gaps, conflicts and undocumented items one by one.
- Until approved, a link is unverified, and impact answers say so.
Rejected: approving every link one by one (a 200-page FSD can produce over a thousand links, which turns approval into
rubber-stamping); people linking everything by hand (slow, and it skips the code checks).
**Why:** code does what it can (candidates, quote and symbol checks); the person's attention goes to the findings.
**Cost:** bulk approval of "agree" is where a wrong AI judgment can slip through; telemetry counts links later
reopened, to show whether it does (D21).
- *Refined 2026-10-20 (Phase 2b build, after a fresh review):* a quote is a sentence, not a fragment: word for word, and
  at least 4 words or a whole table cell, list item or statement. A section whose candidate pages have no recorded
  version is reported as failed, never turned into a gap. "Undocumented by code" only covers the FSD's scope
  (`fsd.scope`): the areas it says it covers, as route prefixes, so an FSD for part of a system doesn't flag the rest.

## D54 · One reference page per screen, plus pages for the backend no screen reaches — 2026-10-19
- **Screen pages:** one per frontend route, with the route as its stable ID (`page:/checkout`). It covers what the screen
  shows, each action → the endpoint it calls → the rules and validations behind it → side effects (events, emails,
  tables written). Every statement cites symbols (D52).
- **Backend pages:** one per entry point no screen reaches (APIs only other services call, event handlers, scheduled
  jobs), grouped per area, with the entry point as the ID.
- **Shared components:** a component used by 3 or more screens gets its own page; the screens link to it.
- **Each page's input is the graph's slice for it:** the walk from its entry point within impact's depth limit (D27).
  That's what keeps each AI call small and grounded.
- Server-rendered screens (Razor/MVC, no frontend route in the graph): the controller action that returns the view is
  the screen. Default until a real repo needs it.
Rejected: screens only (backend-only behaviour such as jobs and service-to-service APIs would go undescribed); pages per
feature (where one feature ends and the next begins needs a person to decide).
**Why:** the IDs come straight from the graph, so they're stable and found by code, not chosen by an AI.
**Cost:** a renamed route changes a page's ID, so its links need the rename repair D10 already plans.

## D55 · The reference catches up per change: detected on each PR, regenerated on merge — 2026-10-19
1. **Every PR, code only:** the graph diff says which pages' slices changed (D54); the PR lists them with their linked
   FSD sections. No AI, no tokens.
2. **On merge to main, AI:** only those pages are regenerated, each version recorded with its commit and input hashes
   (D18). Links whose page text changed drop back to unverified through the text hash (D11) and join the queue.
3. **Per release, optional:** a full sweep regenerates any page whose inputs changed and was missed.
- CI holds a Claude API key as a secret (D24); the per-repo no-AI switch turns step 2 off, leaving step 1.
- Refines D14's "kept current per area": the unit is the page, and the trigger is the merge.
Rejected: regenerating on every push (tokens spent on work in progress); per release only, as the company does (drift is
found weeks after the change, when its author has moved on); a manual regenerate command only (pages go stale when nobody runs it).
**Why:** drift shows up in the PR that causes it, and AI runs once per merged change.
**Cost:** AI calls in CI on every merge that changes behaviour; a page regenerated with an unrelated wording change still
reopens its links (mitigated if regeneration reuses the old page when its inputs' hashes didn't change, D18).

## D56 · Phase 2 is proven on an FSD for Sonarr with planted discrepancies — 2026-10-19
- **Repo:** Sonarr: a real React app whose routes are in the graph (D31), documented screen by screen in the Servarr
  wiki, already in the benchmark with full history (D6).
- **FSD:** written in our own words from the wiki for 10–15 screens, as a `.docx`. *Refined 2026-10-20:* FSDs follow no
  standard (the owner's projects vary), so some chapters are numbered and some are plain prose under headings.
- **Planted discrepancies,** listed in an answer-key file: **gaps** (sections for features Sonarr doesn't have),
  **conflicts** (changed rules, e.g. "at most 3 tags per series" where the code allows any number), **undocumented**
  (real screens or behaviours deleted from the FSD).
- **Scored by code:** planted items found (recall) and unplanted findings raised; a sample of the unplanted ones is
  hand-checked once (some will be real drift between the wiki and the code).
- **Also measured:** tokens and time per page, the D18 check failure rate, and the share of candidate pairs found by
  code vs by the AI (D53).
- After Sonarr, a dry run on a real FSD from one of the owner's projects (summarised, nothing committed).
  *2026-11-03 (owner):* `Documentation.pdf` from the owner's `cyberbullying-detection-system` is treated as a spec agreed with a client
  for import and extraction tests. Its code is Python and JavaScript, so links through the graph can't run on it; those still need
  a C# project with a real FSD.
Rejected: a company FSD first (no answer key, and not cleared yet).
**Why:** an answer key turns "it found 40 discrepancies" into a score; the same reason 1a–1c used compilers and history.
**Cost:** a planted test is tidier than a real FSD (vague wording, one section covering five screens), so it overstates
quality; the company dry run is the correction.

## D57 · Phase 2's done-when: finds what's planted, not too noisy, grounded, repeatable — 2026-10-19
Agreed before any run (as D44, D49), on the Sonarr FSD (D56) with **30 planted items: 10 gaps, 10 conflicts, 10 undocumented**.
| Check | Bar |
|---|---|
| Finds what's planted | ≥ 80% of planted items overall, and ≥ 60% of each kind |
| Not too noisy | ≤ 30% of all findings are false alarms (unplanted, and the hand check says not real drift) |
| Grounded | every page and link reaching the queue passes the D18 checks; tests show a planted fake citation is rejected |
| Repeatable | the same `.docx` → byte-identical text; a re-run with unchanged inputs reuses the recorded AI output (zero tokens) |
| Cost | tokens and time per page reported; no bar yet (D28) |
- **If "finds" or "not too noisy" fails:** reference pages and links still ship, but discrepancy findings are **off by
  default** and the report says why (D44's rule, applied to findings).
- A 95% range (fixed seed, resampling planted items) is reported beside each share, as in D47.
Rejected: stricter bars or 60 planted items (a bigger FSD to write for a first measure); no bars (the bar would drift
toward the result).
**Why:** a pre-agreed bar can't move to fit the numbers.
**Cost:** with 10 per kind, one miss moves a kind's score by 10 points; the range makes that visible.

## D58 · Decisions are fields in a committed YAML file; the website later edits the same file — 2026-10-20
- Each link and finding (D52, D53) is an entry in a committed YAML file: the FSD section, the page, the state, both
  quotes, a `decision` (agree / code-wrong / fsd-wrong / defer) and `decided_by`.
- **Until the dashboard (Phase 8):** a person edits `decision` and opens an ordinary PR. Bulk approval of "agree" is
  one edit over a chapter (D53).
- **The dashboard** is a screen over the same file that opens the same PRs (D12). The file format is the contract;
  nothing is redone when the website arrives.
- **Checked in CI:** the schema and allowed values; both quotes still match the current FSD and page (the D11 hash),
  otherwise the decision drops back to undecided; `decided_by` is filled from git (the PR's approver or merger), never typed.
- "code-wrong" becomes a draft Jira ticket in Phase 3; until then it's listed. *Changed 2026-10-25 (D80): not in
  Phase 3; it stays listed and a person creates the ticket.*
Rejected: bot-opened review PRs with comment commands (machinery the dashboard replaces anyway); an early approval page
(pulls Phase 8 and sign-in forward).
**Why:** the simplest thing that keeps a checked, attributed record, and the website reuses it unchanged.
**Cost:** until Phase 8, reviewing means editing YAML in a PR, which is clunky for non-technical BAs.
- *Refined 2026-10-20 (Phase 2b build):* a decision drops back to undecided when either quote no longer matches its
  section or statement (each quote's hash is stored), not on any edit to the section: a typo fixed elsewhere in a long
  section shouldn't reopen every link in it. An entry's id is the hash of its link and quotes, so an edited quote is
  caught as a hand edit. On merge, CI fills `decided_by` and runs `trace check --fix` in the bot's spec PR.
- *Refined 2026-11-02 (D99, not built):* requirements are approved by tick boxes in the spec-only PR's description,
  not by editing YAML; `decided_by` still comes from git. Trace-link decisions keep this entry's YAML field.

## D59 · Phase 2 is split: 2a reference from code, then 2b FSD and discrepancies — 2026-10-20
| Sub-phase | Delivers | Proven by |
|---|---|---|
| **2a. Reference from code** | pages per screen and backend entry point (D54), every citation checked (D18), AI output recorded and reused, stale pages listed per PR and regenerated on merge (D55) | Sonarr: grounded, repeatable, cost per page; the owner hand-reads ~5 pages |
| **2b. FSD and discrepancies** | `.docx` import, candidate pairs, the AI judge with quote checks (D53), the decisions file (D58) | the planted Sonarr FSD against D57's bars |
- 2a is useful alone: it's the company's model (D50) and the spec of any project with no FSD (D51).
- 2b's conflicts depend on the pages capturing the rules ("max 3 tags"); proving the pages first keeps a 2b failure readable.
- One phase file (`phases/2-spec.md`) and one roadmap row until building starts; split into two files then, as Phase 1 was.
**Why:** the same reason as D29: something working and scored sooner, and a chance to change course before the second half.
**Cost:** 2a has no answer key for page accuracy; until 2b, it's mechanical checks plus a hand-read.

## D60 · AI steps run in Claude Code; the CLI prepares their input and checks their output — 2026-10-20
The first AI steps in the project (2a's pages, 2b's link judgments) and the pattern later phases follow:
1. **`stangent ... prepare`** (code) writes the step's input as one bundle file: the graph slice (D54) or the candidate
   pair (D53), the instructions and the output schema.
2. **A Claude Code subagent from the stangent plugin** writes the output as JSON in that schema. Its tools are limited to
   reading the bundle and writing the result, so it can't wander outside the slice.
3. **`stangent ... check`** (code) validates the schema, the citations and quotes (D18), and records the output with its
   inputs' hashes and the model used. A failed check returns one specific error for one retry.
- **Locally:** run through the plugin's commands on the developer's own Claude Code (D24); no separate API key.
- **In CI** (merge-time regeneration, D55): headless Claude Code (`claude -p`, JSON output) with a token held as a secret.
- **Telemetry** (D21) takes tokens, cost and model from Claude Code's JSON result.
Rejected: the CLI calling the Claude API directly (an API key and its separate billing for everyone who runs it, and a
second way to call AI beside the one every user already has).
**Why:** reuses the tool everyone has; the CLI stays deterministic and testable without an LLM (D9), and every AI output
still passes a code check before anything uses it.
**Cost:** CI needs headless Claude Code and a token; prompt caching and the Batch API's half price aren't directly
available; the subagent's tool limits are a setting, so `check` must still reject anything citing outside its slice.
- *Refined 2026-10-20 (Phase 2 build):* the batch driver enforces the tool limits with permission rules, not only the
  subagent's tool list: `--permission-mode dontAsk --allowedTools "Read(.stangent/**)" "Edit(.stangent/**)"`, so a
  subagent can't read or write outside its working folder (checked: a write elsewhere is refused). `prepare` and
  `check` return the next step (subagent and prompt), so drivers (plugin command, CI, benchmark) hold no logic.

## D61 · Verification by a separate agent with a fresh context — 2026-10-20
Whenever an AI step has to judge whether work is right (reviewing a change, sorting benchmark failures,
checking a report's claims), a **separate agent** does it, starting from a fresh context. It gets the work,
the criteria and the evidence, not the author's reasoning or conversation.
- Applies to every phase, and to how stangent itself is built.
- **Code checks still come first (D18).** A compiler, a test or a schema beats any reviewer. The fresh agent
  covers what code can't check, and its findings are themselves checked against evidence (a file, a test
  result, a line).
- **The author doesn't overrule it silently.** A finding is fixed, or rejected with a stated reason a person can see.
**Why:** an agent reviewing its own work shares the blind spots that produced it and tends to confirm its own
reasoning. A fresh context judges only what's on the page, as a second engineer would (the owner's rule,
in line with evaluator/reviewer patterns from agent builders).
**Cost:** more tokens and time per check; the evaluator needs a brief good enough to judge without the history.
- *Capped 2026-11-04 (owner: "yes cap it", after PDF import took about 1.8M subagent tokens over five checks):* by default each build gets
  **one fresh check and one fix round**, on Sonnet with a narrow brief, with a cost estimate first. What's left is recorded as a known gap or brought to
  the owner; no further round starts on the builder's own call. **Cost:** more edge cases get through, as listed gaps rather than fixes.

## D62 · Phase 2 applies D61: a fresh verifier for pages and for "agree" links — 2026-10-20
Where Phase 2 has an AI step judging whether work is right, a separate subagent with a fresh context does it, after the
code checks (D18):
- **Pages (2a):** `stangent-page-verifier` gets the bundle and the finished page only, and flags statements the slice's
  code doesn't support, quoting the contradicting line. Code checks the quoted line exists; findings go back to the
  writer for its one retry, each fixed or rejected with a stated reason recorded on the page.
- **Link judging (2b):** the judge is already its own fresh subagent, never the page writer's context.
- **"Agree" verdicts (2b)** get a second fresh check before bulk approval; a disagreement turns the link into a finding,
  reviewed one by one. This is the guard D53 named as its cost.
- **The end-of-phase report's claims** are checked against the results files by a fresh agent.
Rejected: a second check on every finding (gaps, conflicts and undocumented items already go to a person one by one).
**Why:** D61 is the owner's rule for every phase; these are the steps where a writer or judge would otherwise grade itself.
**Cost:** roughly double the AI calls per page, plus one per "agree" link; reported apart from the writer's in 2a and 2b.
- *Refined 2026-10-20 (Phase 2a build, after a fresh review):*
  - A "contradicted" finding needs a quoted source line of at least 12 characters and two tokens (`return` proves nothing);
    an "unsupported" finding has no line to quote and is kept. Rejecting an unsupported finding needs the supporting line.
  - The verifier gets one retry when its output is missing or breaks the schema, as the writer does.
  - A page fixed after the verifier's findings isn't verified again: code checks each "fixed" statement changed and each
    rejection has a reason. A second verifier pass would double the cost of every page with findings; reported in 2a.
- *2026-11-04:* "Rejected: a second check on every finding" is reversed by D119, off by default until a paid re-score says otherwise.

## D63 · Slices split at the API boundary; every endpoint gets a page — 2026-10-20
Asked while building Phase 2a, because the plan's slice rule ("the walk from the entry point within impact's depth
limit", D54) doesn't hold on Sonarr: at depth 4 a screen stops at the endpoint, so the rules behind an action aren't in
its page; at depth 6, 125 of ~600 bundles pass 200k characters (one is 868k) and every screen repeats the same backend
rules. "Impact's depth limit" isn't a fixed number either.
- **Screens walk the frontend only and stop at the endpoints they call** (linked, like shared components).
- **Every endpoint gets its own backend page**, as do event and command handlers. This changes D54, which gave pages
  only to entry points no screen reaches.
- **A point with 10 or more callers is a leaf:** included, not walked into (logging, `translate()`, base services).
- Frontend depth 6, backend depth 4, bundle cap 260k characters; all settings in `stangent.yaml` (`spec:`).
- 2b gives an FSD section its screen pages plus the endpoints those screens call (code), so the rules are still in reach.
- Still open to change when 1d part 2 (context scoping) reports (D45): every page then goes stale through its input hash.
**Why:** bounded, layered slices (Sonarr: 718 pages, medians 8k–51k characters, largest 254k); one place per backend
rule instead of one per screen; a backend change makes its endpoint page stale, not every screen that calls it.
**Cost:** more pages (all 364 Sonarr endpoints, many near-duplicates across API v3 and v5); a screen page alone doesn't
tell the whole story of an action.

## D64 · The split check is judged on bitwarden's first run; no rerun — 2026-10-20
The plan asked both repos to run to 30 split cases or 200 examined, with every failure counted.
- **What happened:**
  - **bitwarden/server** reached 30 split cases (116 examined), but the tool's hunk cutter had bugs that added
    failures. A fresh agent (D61) sorted the 14 failed splits: 8 were the tool's cuts, now fixed, and 6 were real.
  - **Sonarr** crashed on a .NET runtime segfault after 32 examined cases. That run used the same buggy cutter.
- **Decision:** don't rerun. Judge "safe" from bitwarden's real failures alone. "Useful" is left unmeasured: the
  bug's false splits inflate the first run's 26%. A re-grouping with the fixed cutter was started, then stopped by
  the owner (D65).
  - "Safe" must pass **on each repo**. On bitwarden, at most (16 + 8) / 30 = 80% of the split cases could hold up,
    even if every tool-made failure had passed. That's below the 90% bar.
  - "Better than naive" isn't met as specified, whatever the cutter does: the graph is safer, but split-by-folder
    splits 98% of cases against the graph's 26%, far over the 5-point condition.
  - So no rerun can turn the verdict into a pass. Sonarr's partial run is reported, not judged.
- The cutter is fixed and tested (d770578, b7e90e9) for whoever uses the harness next, for example to test a
  Phase 4 split rule.
**Why:** a rerun is about 22 hours of building and testing to confirm a fail the real failures already show. The
tool's bugs added failures and false splits, so they can't have raised the safe share. A rerun walks the same commits newest first, so
the 6 real failures would very likely fall among its first 30 split cases, and 90% of 30 allows at most 3.
**Cost:**
- The numbers come from a run with known bugs, so they are bounds, not measurements.
- Sonarr has no judged result.
- If the bar had been close to passing, this shortcut wouldn't have been allowed.

## D65 · The graph's blind spots and a smarter split rule are Phase 4 work — 2026-10-20
The split check (Phase 1d) found two reasons the graph can't yet split a change safely on its own:
- **What it doesn't see:**
  - Razor views (`.cshtml`);
  - `.csproj` and `Directory.Build.props` files, and package versions;
  - `using` edits that change what a name means.
- **What it likely over-joins (inferred, not measured):** unchanged hub types (`Organization`, `User`) may link
  unrelated edits within 2 steps. That's suggested by direct-only splitting 3× as often on bitwarden.

Both go into **Phase 4**, not a separate graph phase:
- The graph learns those files.
- The splitter joins only through points that changed or that the change affects. Contract-first splits are
  considered with it (see the open question).
- Each change is proven with the 1d harness (`--split`) before Phase 4 relies on it.
- The merged build and test stay, whatever the graph learns (D18). Some links (a test counting routes) are in no graph.

Rejected: a small "1e" graph phase first. It would help Phases 5 and 6 sooner, but the owner chose to keep the work
with the phase that needs it most.
**Why:** Phase 4 designs the splitter, so it can shape the graph work around what the splitter needs. All of it is
plain code (no AI tokens at runtime).
**Cost:** Phases 5 and 6 get the better "what may break" only once Phase 4 lands; Phase 4 grows.

## D66 · Phase 2a is proven on the pages 2b needs plus a sample; Sonnet writes and verifies — 2026-10-20
*Numbered D64 on the Phase 2 branch; D66–D68 there were D64–D66 until main's D64 and D65 were merged in (2026-10-25).*
The first full Sonarr run used far more than estimated: 193k input tokens per page on average (two headless Claude
Code sessions per page, each re-sending its system prompt and the bundle chunks every turn), about $0.31 per page at
list price, ~$220 for all 718 pages; it hit the owner's usage limit twice.
- **Pages generated for the proof:** every page 2b's candidates need (the FSD's in-scope screens and the endpoints and
  components they link to, ~103), plus a fixed sample of 10 event/command handlers (seed 42). The rest (~550, mostly
  endpoints, half of them v5 copies of v3, and handlers) is left to `spec-run.sh --all`.
- **Every page is still checked by code without AI:** each of the 718 fits under the size cap or is listed as failed.
- **Calls become single-turn first** (the bundle in the prompt, structured output, no tools), measured on 10 pages.
- **Model:** Sonnet 5.5 writes and verifies. The owner's 5-page hand-read decides whether the writer needs Opus.
  Weakening the verifier to save tokens was rejected: it's the guard D62 relies on.
- Changes 2a's done-when box "every Sonarr screen and backend entry point has a page" to the set above; decided by the
  owner before the bar was measured, not to fit a result.
**Why:** the other ~550 pages only show that more of the same works; the questions Phase 2 answers (does the FSD
comparison find planted drift, are pages grounded, what do they cost per kind) are answered by the smaller set.
**Cost:** Sonarr has no complete reference; per-kind cost is measured on smaller samples (10 handlers).
- *Refined 2026-10-20 (single-turn calls, measured):* the batch driver sends each step as one call: input in the prompt,
  `--tools ""`, `--json-schema`, no settings, skills or MCP servers (a bare Claude Code call carried 41k tokens of
  its own context; now 1.2k). On the same 10 pages (6.5k–152k characters, Sonnet 5.5, before and after in
  `benchmark/results/2026-10-20-phase2a-singleturn-measure.json`): **41k input tokens per page against 200k, $0.18
  against $0.31, 111 s for all ten at 3 jobs**; the same verifier findings or one more or fewer per page. The cost left
  is mostly the bundle written to the prompt cache once for the writer and once for the verifier (their schemas
  differ, so they can't share it); cost ≈ $0.035 + $4 per million bundle characters. The plugin's subagents stay for
  interactive use and as `spec-run.sh --agents`.
- *Refined 2026-10-25 (the writer model):* the 5-page hand-read, made by a fresh agent instead of the owner (D67),
  found 2 wrong statements in 88 (one inverted condition, one wrong endpoint link); the verifier caught neither.
  **Sonnet stays the writer;** the owner had asked for the hand checks to be decided for him.

## D67 · Phase 2's hand checks are done by a fresh agent, not the owner — 2026-10-24
D56, D57 and 2a planned two checks by a person: 20 unplanted findings marked real drift or false alarm (the "not too
noisy" bar), and 5 pages hand-read for wrong statements. The owner asked for them to be decided for him (Sonarr is a
demo); decided before either check was run.
- **A separate agent with a fresh context** does both (D61): it gets the criteria, the FSD text, the pages and the
  Sonarr source at the pinned commit, nothing about how the pages or findings were made. It writes one verdict per
  finding and one count per page, each with its reason and evidence (a source line or FSD sentence).
- **Not the author:** the session that built and ran the pipeline doesn't mark its own findings.
- The reports say the checks were made by an AI checker, and the bar is read with that caveat.
Rejected: the author deciding (it would grade its own work, D61); skipping the checks (the noise bar would stay
unmeasured).
**Why:** keeps the noise bar measured by someone other than the author while the owner's time goes elsewhere.
**Cost:** an AI checker can share the judge's blind spots; a person's check is still owed before the findings are
relied on in a real project (the dry run on a real FSD, D56, is checked by people).

## D68 · Discrepancy findings ship off by default (D57's rule, applied) — 2026-10-25
On the planted Sonarr FSD, "finds what's planted" passed (27 of 30, each kind ≥ 60%) but "not too noisy" failed: 31.3%
of findings are false alarms (95% range 14–45%) against a bar of 30%, from a hand check of 20 unplanted findings by a
fresh agent (D67). D57 says what follows: reference pages and links still ship; findings are off by default.
- **`fsd.findings` in `stangent.yaml`, default `false`:** sections record only links ("agree", confirmed or not);
  gaps, conflicts and undocumented items, including disputed agrees turned into findings, aren't written to
  `specs/trace.yaml`, and `fsd undocumented` lists none. A project turns them on with `findings: true`.
- Turning the setting off or on makes each section run again (it is part of the section's input hash only when off,
  so runs recorded with findings on stay current). The Sonarr benchmark turns them on: it measures findings.
- The bar isn't moved: the report states the result and why findings are off.
Rejected: shipping findings on because the miss is narrow (the bar would drift toward the result, D57); dropping
the agree checker's disputes to cut noise before re-measuring (tuning to the frozen key; a change is a new benchmark run).
**Why:** D57, agreed before any run.
**Cost:** a project that wants the drift list must opt in and accept roughly one false alarm in three; the likely
lever (the agree checker turning "partial" agreements into gaps, 25 of the 62 unplanted findings) is a follow-up.

