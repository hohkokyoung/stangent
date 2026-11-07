# Open questions

Only what's still open, sorted by when it needs answering (D136). When one is answered: record the decision, then move the question to [`decisions/answered-questions.md`](decisions/answered-questions.md) in the same turn.

## Waiting on the owner now

- **Use cases run by a script, or in a component with no page (found 2026-11-07, cyberbullying):** 3 of 4 use cases run from a background script, which the graph has no page kind for, and Logout lives in a navigation component. Recommended: first, write pages for components a screen reaches (Logout via `/dashboard`) with `--for-fsd`, then ask the finder again; a page kind for scripts' entry points only if a second project needs it. **Cost:** until then those sections can only give gaps nobody can judge.
- **Flow titles that state a rule (found 2026-11-07, Login with `claim-links`):** D143 sets aside a flow's title, and on Login one title holds the use case's only conflict. Recommended: judge a flow title as a claim when it's a sentence, not a name. Before that, run the same judge headless with `claim-links` off, about $0.30, to see whether the lost findings are `claim-links` or headless one-turn calls.
- **`.stangent/` isn't always ignored by git (found 2026-11-05):** in the cyberbullying clone `.stangent/` shows as untracked, so it could get committed by accident. `docs/spec.md` used to say `fsd export`'s default folder is always ignored. That's fixed (D146): it now says only `stangent init` adds the line, and `ignoredByGit` in the result reports it. Still the owner's call: whether `stangent` should add the line to the repo's ignore list by itself on first use.
- **A decision is lost when a re-judge quotes less of the same statement (found 2026-11-05, `phases/2c-spec.md`):** a link's id follows the page quote, so on Login 2 of 5 decisions went `gone` and came back as new undecided findings, with the page statement unchanged. Recommended: make an undocumented link's id follow the statement's id, with the switch off until tested, since it moves saved ids. **Cost:** ids change, so it needs `SavedWorkTests`' pins kept with the switch off. **Built, off and untested with a real AI (D145, 2026-11-06):** `claim-links` makes every link's id from claim and statement ids, for all states and not only undocumented ones, and carries old rulings when a section is next judged. A test reproduces the bug with fakes. Still to do: judge a section with rulings twice on a real AI, which costs a re-judge of what it reaches.
- **Under D137, who answers "is it true?", and who confirms what it throws away? (raised 2026-11-05):** D119 has `refuted` and `unreachable` wait for a person, and the PM shouldn't be that person. Recommended: decide after the disprove re-score shows whether the step works at all. Until then, every finding still reaches a person.
- **Ask for a PM's ruling only when a ticket reaches the section? (assistant's idea, 2026-11-05; the owner hasn't answered):** findings stay marked unconfirmed until then, which the bundle already handles. On Login, D137 leaves about 3 or 4 questions, but ruling on every section is still work a PM may never need. **Cost:** a wrong document or a real bug stays unnoticed until a ticket lands there, so Stangent stops working as an audit of the whole spec.

- **Compare the FSD directly with the code, not through pages? (owner, 2026-11-05: "maybe we should just turn off the reference pages part, lets directly compared to the parse fsd and the code, isnt that what the improvement i told u says?"; "Yes, try on Login"; not decided):** `improvements.md` §6 asked for it and it was never built. The "noisy" reason recorded for keeping direct links out doesn't appear to come from a test of direct comparison: D68's 31.3% was measured through pages. One hand-fed test on Login gave 5 findings against 13 and no false pop-up gaps, counts in `phases/2c-spec.md`. **It can't yet say pages are the fault,** since the code was picked by hand and included the pop-up, which the judge through pages never saw. **Compared three ways the same day, one section, one run each, hand-fed:** 13 findings today, 6 with the pop-up's page added and no false alarm on the assistant's reading, 5 direct with 1 false. So on this section the missing page was the cause, not pages. Recommended now: keep pages, and reach a screen's shared components through its unshared ones, D127's open question, off by default and tested on more use cases with the checker on. This reopens D131's removal of `shared-candidates`, whose one step didn't reach the pop-up. **Cost:** more pages to write and more for the judge to read: on Actual, 57 of 71 screens have a shared component one step away, more at two. Direct stays untried for finding what a wrong page hides.
**Built the same day (owner: "sure do that now"; D140):** the recommendation above, as `shared-through`, off by default. Section 7.2.1.2.4 now gets the pop-up by code, and 61 of 300 sections gain it. On Actual 52 of 71 screens gain, 6.4 on average and up to 22, so the cost above is larger than one step's. **Untested with a real AI:** no judge has run with it, so it hasn't shown that the false gaps go when code picks the page. This question stays open.
**Corrected the same day (owner: "but the pages is useless no?"):** "keep pages" was too strong for the comparison. Direct's one false alarm came from a CSS file left out by hand, and the pages route's matching `agree` had no words on the page behind it, so on accuracy this section is about a tie. On Sonarr's saved runs, page writers and verifiers wrote 7.62M tokens to the cache against 0.28M for the links, so pages are by far the costliest piece, and nothing has measured that they help a fix. They do let someone who can't read code rule on a finding, and a decision rests on a statement's words (D118). Still open. Recommended: run both routes on another use case, with code choosing what's sent, before deciding.

- **A first real pilot, a cut rule, a cap and an earlier bundle test (proposed by the assistant, 2026-11-04; the owner: "ok what we gonna do next, and lets do it in a new sessin"; not numbered until confirmed; order on `roadmap.md`):**
  (a) **The next milestone is a first real pilot** on the owner's own project, even with no FSD: pages, impact, one real ticket. Nothing added lately has met a real FSD or a real project, and the core result is still 24 of 30 found with 44% false alarms. Free steps first; AI steps on the owner's OK.
  (b) **Cut rule:** decided, D138.
  (c) **Cap on new items:** rejected, D139.
  (d) **"Does the bundle help a fix?" runs earlier:** in the test phase, before Phase 4 is grilled, not with it. Every Phase 3 bar checks a piece; none shows the bundle helps. Paid, on the owner's OK.
  **Order changed by the owner (2026-11-05: "wy not just build more first and we measure each stage later"):** I and T are built before the pilot, as D120 already says. Assistant's call, reversible: T.9, T.10 and I.12 wait for a repo or document that needs them; X items stay off. **Cost:** none of I or T can move 24 of 30 or 44%, so the core result stays unmeasured on a real spec for another 500-700K tokens, and defects stay in longer (D120's cost).
- **A bigger spec for the test material (owner, 2026-11-05):** recommended, not confirmed: `umbraco/UmbracoDocs` beside Umbraco itself. Details under "Which project the pilot runs on" in [`decisions/answered-questions.md`](decisions/answered-questions.md).
- **May a "nearby" FSD link count toward "working as agreed"? (asked 2026-11-02, not answered):** recommended: no, context only, a person decides. Details under "Ticket retrieval redesign" in [`decisions/answered-questions.md`](decisions/answered-questions.md).
- **Recommended, not confirmed (2026-11-04):** BAs editing through Claude, not yet; journey pages, answered by D144; table comparison as a labelled second route, yes; installer, when BAs become users; keep `MaxChapterGap = 10`; `improvements.md` as the agreed list, yes.
- **Waiting on the owner after the overhaul (2026-11-04):**
  (a) Confirm `improvements.md`, and the order after 2.x.
  (c) The disprove step's re-score, about $4–6 (`phases/2c-spec.md`).
  (d) 2c's found bar: the disprove step can only remove findings, so it can't lift 24 of 30 to 27, and after it found is 19. **The re-score is in (2026-11-07):** noise bar met at 23%, found not. Recommended: fix `page-gap` first (above), which should bring found back toward 24; then keep the bar and look for what's missing (the gap finders), since 24 isn't 27 either way. D138's cut rule then applies to found, the owner's call.
  (e) BAs editing the FSD through Claude (reversing D52 or D12), dropping the requirements layer (D99), left alone, as agreed. Journey-based pages: answered by D144, workflow files on top of pages.
  (g) A client-terms file outside the repo, so `data check` can look for the names it must not find.

## In the test phase

- **Table facts compared by code, a second route to code (proposed 2026-11-04, `improvements.md` §8 C.2; built switched off and labelled, D124, so this is still open):** everything else reaches code through pages, and D68 kept direct FSD-to-code links out as noisy. Recommended: allow exact comparison as its own labelled route, kept out of the impact walk like 📜 lines, after a pilot on field lists with a real document.
- **Evidence beyond the FSD for "looks intended" (owner's outside review, 2026-11-01):** API docs, existing tests that
  assert the current behaviour. Not decided. (The review's other new idea, right place reported per stage, is built:
  `reported.rightPlaceStages`, D98's re-score.)
- **Does the bundle help a fix, or skew it? (owner's questions, 2026-11-02, for Phase 4):** every Phase 3 bar checks a piece (a
  right file, a real quote, a failing test); none shows the bundle helps anyone fix a bug. On the finder's 4 Sonarr misses it
  covers none of the fix's files. The owner's worry: a wrong lead skews every later step, and a scoped input invites a less clean
  fix (SONB-18: a null check on the screen turns a test green and leaves the bad data). Recommended: the fix agent on the same bugs three ways
  (the ticket alone; plus the failing test; plus the whole bundle), scored on the tests passing and on a read of each diff against the developer's fix, wrong leads
  counted apart; if there's no difference, cut it to the finder's files and the failing test. Recommended rules: named files are
  leads, never a limit on what an agent reads or edits; it traces from the failing test before it edits. A green test can't show
  a fix is clean, so that stays a person's review (Phase 6). To grill with Phase 4.
  **Proposed on top (recorded on the owner's "ok yes", 2026-11-02; not decided):**
  (a) **Start without the bundle:** the fix agent gets the ticket and the code until a test shows the bundle helps. Reference pages
  are generated from code, so they can describe a bug as how the feature works; the ticket and the agent can skew a fix too (SONB-18's
  title invites the null check), so this is measured against them, not assumed.
  (b) **Label context by source:** something that ran; something a person or the FSD said; something read in the code; a guess. No fix
  rests on a guess until a run or a person backs it up.
  (c) **Plant wrong leads:** the fix agent on the 17 Sonarr bugs whose saved test passes on the developer's fix, with and without a deliberately
  wrong file or cause. Code runs the tests; a person reads only the fixes that go green, since a green test can still be a wrong fix. About $8–34
  for 34 runs, estimated from the proof's cost (no fix agent has run), on the owner's OK. The saved tests predate the finder and may need rechecking.
  Sonarr is public, so the result may look better than on a private repo.
- **Flakiness of the proof (2026-11-02):** only one full run, and a unit test runs once. Running a test more than once per proof
  needs no AI. Not decided. The Sonarr re-run gives a second result to compare.
- **The not-bug bar on Sonarr (proposed 2026-11-02, not decided):** report D79's "catches wrong reports" as "can't be
  measured: no FSD covers these tickets" rather than failed, unless the planted FSD covers them (a free check: which of
  the 10 not-bug tickets fall inside its 15 screens). **Checked 2026-11-02: it covers none.** The 10 are about an
  indexer, a required password, library import, anime parsing, table sort memory, bulk moves, memory, CPU and disk space;
  the closest, SONB-42 (a Trakt link on series details), lands in §1.2, which never mentions links. So D99 can't be
  measured on Sonarr's not-bugs; it needs a project with a real FSD, or FSD text written for those areas.

## With a later phase

- Parallel subagents: should the orchestrator fix the contract first (API shapes, shared types), then run
  the sides in parallel against it? And if a subagent finds mid-task that the contract must change, does it
  stop and hand back, or change it and tell the others? Leading answer: contract first; stop and hand back.
  Grilled with Phase 4, after the 1d verdict. (Phase 4)
- "ministack" vs LocalStack vs Testcontainers for integration tests: was it a deliberate choice? (Phase 5)
- Dashboard sign-in for BA/PM: company SSO or GitHub accounts? (Phase 8)

## When something needs it

- **Serving other stacks (owner, 2026-11-04: "maybe we can try pivot our project to also cater to other tech stack ... the fundamentals is the same right"; open):** partly the same. By lines of C#, `Stangent.Spec` (11.7K) doesn't use Roslyn and one of its files mentions a stack's file shapes. What's tied to a stack: `Stangent.Graph` (4.3K lines), the TypeScript extractor (1.1K), six `Stangent.Tickets` files (mostly the proof, which builds and runs the app). What doesn't carry over: quality. Roslyn resolves calls exactly; a typed language such as Java should come close, a dynamic one such as Python won't, and impact and page slices rest on those calls. 24 of 30 and every other score were measured on C#. Recommended: keep the goal, don't pivot now. Finish a pilot on what's supported first (D1's reason: abstracting over many stacks before one works end to end is how platforms stall), and add a language when a test needs it, as the proposed cap says. Rough estimate, from past batches: 500-900K tokens for one language's extractor and detectors. D4 and D17 stay until the owner decides.
- **For the Playwright adapter (T.9, 2026-11-04):** no project of the owner's to ask about. On Umbraco the tests sit in the app's repo and use a helper package, so helpers get a real test. **Specs in another repo do have a known case after all:** RealWorld's Playwright tests sit in its spec repo, apart from every implementation. D4 leaves separate repos out for now, so taking this up is the owner's call.
- **Generality batch (2026-10-31):** fixed as D94 (2026-11-01), except Karma/Jasmine and Mocha test targets; still to
  check on a third repo of a different shape (CleanArchitecture).
