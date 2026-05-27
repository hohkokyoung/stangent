---
description: Create or manage Architectural Decision Records (ADRs)
argument-hint: "new <title> | list | accept <id> | supersede <old-id> <new-id> | bootstrap [--max N]"
---

# /agentic-adr

Manage project-level architectural decisions. ADRs override skill defaults and bind every future task once `status: accepted`.

## Subcommands

### `new <title>`

1. Allocate id: `python .claude/hooks/lib/adr_id.py next` (e.g. `ADR-003`).
2. Create `.claude/adrs/<id>-<slug>.md` by copying `.claude/templates/adr.md`.
3. Set frontmatter: `id`, `title`, `status: proposed`, `date` (UTC YYYY-MM-DD), `supersedes: null`.
4. Print the path and tell the user: *"Edit it, then re-run `/agentic-adr accept <id>` to make it binding."*

### `list`

Read every file matching `.claude/adrs/ADR-*.md`. Print a table:

```
ID       STATUS       TITLE                                SUPERSEDES
ADR-001  accepted     All timestamps are UTC               -
ADR-002  proposed     Money stored as integer cents        -
ADR-003  superseded   JWT in cookie                        replaced by ADR-005
```

### `accept <id>`

Edit `.claude/adrs/<id>-*.md` frontmatter: `status: proposed` → `status: accepted`. Refuse if the file is missing or if it's already accepted/superseded.

### `supersede <old-id> <new-id>`

1. Verify both files exist; `<new-id>` must be `status: accepted` or `proposed`.
2. Edit `<old-id>`'s frontmatter: `status: superseded`.
3. Edit `<new-id>`'s frontmatter: `supersedes: <old-id>`.
4. Print confirmation.

### `bootstrap [--max N]`

Scan the existing codebase and propose ADRs for recurring architectural patterns. **Every proposed ADR is `status: proposed`** — nothing becomes binding until you `/agentic-adr accept <id>` each one. Default `--max 10` proposed ADRs per run.

#### Procedure

1. **Skip if no codebase to scan.** If the project has < 5 source files outside `.claude/`, print "not enough code to bootstrap; come back after the project has some patterns to detect" and stop.

2. **Skip already-detected patterns.** Read every existing `.claude/adrs/ADR-*.md` (any status). Build a set of decision-summaries (the `## Decision` first line of each). Bootstrap MUST NOT propose an ADR whose decision overlaps an existing one — paraphrase-equivalents count as duplicates.

3. **Sample the codebase systematically.** Use Glob/Grep/Read (read-only — no Edit, no Write outside `.claude/adrs/`). Cover at least:
   - Project config: `package.json`, `pyproject.toml`, `requirements.txt`, `pubspec.yaml`, `tsconfig.json`, etc.
   - Schema / migration files: `*.sql`, `prisma/schema.prisma`, `migrations/**`.
   - Auth / middleware files (search for `Bearer`, `cookie`, `session`, `jwt`, `Authorization`).
   - Error-handling patterns (search for `HTTPException`, `raise`, `throw`, error-response shapes).
   - Time / money handling (search for `datetime`, `timestamp`, `timezone`, `cents`, `decimal`, `Money`).
   - Logging (`logger.`, `console.`, `print(` — what gets logged, what doesn't).
   - Test conventions (`test_`, `.test.`, `_spec.`).
   - 5–10 representative source files from the main source dirs.

4. **Apply the detection checklist.** For each dimension below, look for a recurring choice with **≥ 3 supporting code locations**. Fewer than 3 = not enough signal, don't propose.

   | Dimension | Question | Example detection |
   |---|---|---|
   | Timestamps | Are timestamps stored / serialized consistently? | `timestamptz` everywhere, ISO-8601 with `Z` in responses |
   | Money | Is money represented uniformly? | Always integer cents, never float |
   | IDs | UUID? auto-increment? prefixed? | `uuid_generate_v4()`, `user_xxx` prefixes |
   | Auth | How are requests authenticated? | `Authorization: Bearer <jwt>` everywhere |
   | Error responses | Same shape across endpoints? | `{"detail": "..."}` always |
   | Validation | Where does it happen? | Pydantic at boundary, no manual checks inside |
   | Logging policy | What's never logged? | No PII appearance in any logger.* call |
   | DB access | Direct from clients, or only via RPC? | All client reads go through RPC functions |
   | RLS | Every user-data table has RLS on? | `ENABLE ROW LEVEL SECURITY` in every user-table migration |
   | Secrets | Where do they live? | `os.environ` reads only at startup; never inside handlers |
   | Dates in URL | ISO format only? | All path params look like `2026-05-27` |
   | Pagination | Cursor or offset? | Cursor-based on all list endpoints |
   | Background jobs | Same library / pattern? | `arq` for all async work |
   | Frontend state | Riverpod codegen everywhere? | All providers use `@riverpod` annotation |

   Don't limit to the table — add a dimension if you spot a clear cross-cutting pattern.

5. **Score and rank.** Each candidate gets a confidence score:
   - 3 instances = weak (don't propose unless few candidates total)
   - 5+ instances = solid
   - 10+ instances = strong
   Propose strongest first, up to `--max N`.

6. **Draft each ADR.** Allocate id with `python .claude/hooks/lib/adr_id.py next`. For each candidate write `.claude/adrs/<id>-<slug>.md`:
   - Frontmatter: `status: proposed`, today's UTC date, `supersedes: null`.
   - `## Context` — *why this is worth pinning*, plus a short evidence list (3–5 concrete file references like `src/api/users.py:42`).
   - `## Decision` — single-sentence rule.
   - `## Consequences` — likely consequences, including any obvious tradeoffs you noticed.
   - `## Anti-patterns` — the inverse choice that some code might already violate (note the violating file if you saw one).

7. **Print a summary table** at the end:

   ```
   Proposed N ADRs (all status: proposed). Review each, then /agentic-adr accept <id>.

   ID        TITLE                                 EVIDENCE      VIOLATIONS-FOUND
   ADR-001   All timestamps stored as timestamptz  12 sites      0
   ADR-002   Money stored as integer cents         8 sites       1 (src/billing/invoice.py:67)
   ADR-003   Errors return {"detail": str}         15 sites      2 (src/api/orders.py, src/api/auth.py)
   ```

#### Constraints

- **Read-only on source code.** Only writes go to `.claude/adrs/`. Never edit application files.
- **Always `status: proposed`.** Never auto-accept. Even strong-confidence patterns get human review.
- **Never propose what already exists.** Even if a pattern is genuinely present in 50 files, if an ADR already covers it (in any status), skip.
- **Bias toward fewer, stronger ADRs.** It's better to propose 3 high-confidence ones than 10 noisy ones — the user will reject noise and lose trust in bootstrap.
- **One pass.** Do not loop / re-scan. If you missed something, the user can run bootstrap again later.

## Constraints

- Never delete ADR files. Superseded ADRs stay for traceability.
- Never modify a `superseded` ADR's body — write a new ADR instead.
- All edits go through these subcommands or are manual file edits — no agent invocation needed.
