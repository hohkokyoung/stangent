# Efficiency Rules

These rules are binding for every Stangent agent. They exist to cut token
churn — most of the cost in a pipeline run comes from re-reading the same
files and re-writing entire spec sections instead of editing. Follow them.

Read this file **once** at the start of your run, then apply silently. Do
not re-read it between phases.

---

## Rule 1 — Read once, cache mentally

Each of the following is read **at most once per agent run**. After the
first read, reason from what you already have in context. Do not issue
another Read for any of them unless a later step has invalidated the cache:

- `.stangent/config.json`
- `.stangent/decisions.md`
- `.stangent/SRS.md`
- `.stangent/memory.md`
- `.stangent/meta.md`
- The feature spec file (`{feature_file_path}`)
- Any file under `.stangent/prompts/`
- Any file under `.stangent/profiles/`

If you need a small piece of one of these again later, recall it from
context — do not re-Read.

---

## Rule 2 — Grep before Read

Locate before you load. The default discovery sequence is:

1. `Grep` with `output_mode: "files_with_matches"` to find candidate files.
2. `Grep` with `output_mode: "content"` and `-n -C 3` (or similar) to
   confirm the match line and immediate context.
3. Only then `Read` the file — and only if step 2 was not enough.

Use `Glob` (not `Bash find` / `Bash ls`) for file discovery by pattern.

Do not slurp a whole file just to find one symbol.

---

## Rule 3 — Read narrowly

For any file larger than ~5 KB, use the `Read` tool's `offset` and `limit`
parameters to read only the lines you need. The match line from a Grep with
`-n` tells you exactly where to start.

Whole-file reads are reserved for small files (< 5 KB) or for files where
you genuinely need the entire contents (the feature spec itself, a profile
file you must apply in full).

---

## Rule 4 — Edit, never Write, for incremental updates

`Write` re-sends the entire file contents every time. `Edit` sends only the
diff. For any file that already exists and you are updating in place,
**always use Edit**.

`Write` is allowed only when creating a new file from scratch.

Concretely:

- Ticking an Acceptance Criteria checkbox → `Edit` (`- [ ]` → `- [x]`).
- Appending to `## Implementation Log`, `## Files Changed`,
  `## Future Considerations`, `## Review Verdict`, etc. → `Edit`, anchored
  on the next section header below.
- Updating frontmatter fields → `Edit` on the specific line.
- Rewriting a single spec section after revision → `Edit`, anchored on the
  section header and the next header.

If you find yourself about to call `Write` on an existing file, stop and
restructure the change as one or more `Edit` calls.

---

## Rule 5 — Don't re-read the spec between phases

The feature spec is loaded once per run (Rule 1). Each phase that produces
new sections appends them via `Edit`, anchored on a known string already in
the file (typically the next section header). Do not reload the file
between phases just to "see the current state" — your in-context copy is
authoritative for the duration of the run.

If another agent has written to the spec in the meantime (e.g. you are
resuming after a sub-agent run), one targeted re-read of the changed
section is allowed. Not the whole file.

---

## Pre-tool-call checklist

Before every `Read`, `Write`, `Edit`, `Glob`, or `Grep` call, mentally
confirm:

1. Have I already loaded this file (or this information) in this run? If
   yes, do not re-issue.
2. If I'm reaching for `Read`, could a `Grep` answer this instead?
3. If the file is > 5 KB, am I passing `offset` + `limit`?
4. If I'm reaching for `Write` on an existing file, can I `Edit` instead?
5. If I'm about to re-read the spec, am I sure I need to — or am I just
   double-checking something already in context?

If any answer is "no" or "I'm not sure", reconsider before issuing the call.
