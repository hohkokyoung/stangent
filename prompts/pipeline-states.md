# Pipeline States

Every feature has a `status` field in its frontmatter. Valid states and transitions:

| State | Meaning | Valid next states |
|-------|---------|-------------------|
| `CREATED` | Initialized, no spec yet | PLANNING |
| `PLANNING` | Planner writing the spec | AWAITING_CONFIRMATION, PAUSED, FAILED |
| `AWAITING_CONFIRMATION` | Spec done, waiting for developer | CONFIRMED, ABANDONED |
| `CONFIRMED` | Developer approved spec | IMPLEMENTING |
| `IMPLEMENTING` | Implementer writing code | REVIEWING, PAUSED, ESCALATED |
| `REVIEWING` | Reviewer checking implementation | REVIEW_PASS, IMPLEMENTING (retry), PAUSED |
| `REVIEW_PASS` | Review passed | SRS_UPDATE |
| `SRS_UPDATE` | SRS agent documenting | COMPLETE |
| `COMPLETE` | Done, branch ready for PR | — terminal |
| `PAUSED` | Waiting for developer input | resumes to prior active state |
| `BLOCKED` | A dependency is not COMPLETE | PLANNING (after deps complete) |
| `ESCALATED` | Max retries exceeded | — terminal, developer must intervene |
| `FAILED` | Agent error (not review FAIL) | — terminal, developer must investigate |
| `ABANDONED` | Developer cancelled | — terminal |

**Ownership rules:**
- Only the orchestrator may change `status` in frontmatter
- Exception: individual agents may set `status = PAUSED` when waiting for developer input
- ESCALATED, FAILED, and ABANDONED are terminal — never auto-resume
- A review FAIL does not set FAILED — it sets retry_count and returns to IMPLEMENTING

**State reading:**
The `status` field is in YAML frontmatter (between the first two `---` lines).
Parse it by finding the line starting with `status:` in that block.
Do not rely on Markdown headings to find status.
