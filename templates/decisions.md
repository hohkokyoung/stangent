# Architecture Decision Records

Every architectural decision made during development is recorded here.
All Planner and Implementer agents read this file before starting work.

**Rules:**
- Never delete an entry. Mark it `Superseded` with a reference.
- Decisions here are binding. Implementers must honour them.
- If a new feature conflicts with a decision: the planner will surface it
  automatically and ask for resolution (comply / override with reason / cancel).
  Overrides are recorded per-feature in the feature spec — the ADR itself
  stays Accepted and continues to govern all other features.
- ADRs are auto-bootstrapped on first `/feature` by scanning the codebase.
  Use `/adr <title>` to add explicit decisions at any time.

---

## How to Read This File

Each ADR answers: what did we decide, why, and what does it mean going forward.
Check this file before asking the developer architectural questions.
If your question is answered here, do not ask again — apply the decision.

---

<!-- ═══════════════════════════════════════════════════════════════════════
     ADR TEMPLATE — copy this block for each new decision
     ═══════════════════════════════════════════════════════════════════ -->
<!--
## ADR-XXX: Decision Title
**Date:** YYYY-MM-DD
**Status:** Proposed | Accepted | Superseded by ADR-YYY | Deprecated
**Feature:** FEAT-XXX | Project Setup | Manual | bootstrap
**Raised by:** developer | planner | implementer | bootstrap

**Context:**
Why this decision was needed. What problem it solves. What constraints existed.

**Options Considered:**
- Option A: description — pros / cons
- Option B: description — pros / cons
- Option C: description — pros / cons

**Decision:** What was chosen.

**Rationale:**
Why this over alternatives. What tipped the balance.

**Consequences:**
- What future agents/developers must do as a result
- What is now prohibited
- What is now the default approach
-->

<!-- Add ADRs below this line, newest at the bottom -->
