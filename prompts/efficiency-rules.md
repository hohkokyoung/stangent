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

---

## Prompt-Caching Invariants

Stangent itself does not call the Anthropic API directly — every agent run
happens inside the Claude Code harness, which auto-caches the stable
prefix of each prompt (system prompt + tool schemas + agent definition)
for ~5 minutes. To keep cache hit rates high across consecutive runs,
preserve the following invariants when editing agents or prompts:

1. **Stable prefix, variable suffix.** Inputs that change per run
   (`feature_id`, `raw_request`, `corrections`, `revision_context`,
   `previous_verdict`, `failure_type`, `tier`) belong in the spawn
   template's `INPUTS` block — at the **end** of the prompt, never inlined
   into the agent definition or the role/constraints sections.

2. **Don't rewrite frontmatter or role text casually.** Any edit to the
   first ~80% of an agent file (frontmatter through PROCESS) invalidates
   the cache for every subsequent spawn until the next 5-minute window.
   Save trivial wording cleanups for batched releases.

3. **Don't add per-feature data to shared prompt files.** Files under
   `.stangent/prompts/` (this file, `load-profiles.md`, `write-contract.md`,
   etc.) are loaded as part of the stable prefix. They must contain rules,
   not feature-specific content.

4. **Read prompts files once per run** (Rule 1). The harness caches the
   first read; a second Read in the same run will hit fresh tokens
   anyway because the surrounding context has shifted.

5. **Spawn template fields are ordered.** Do not reorder the
   `INPUTS / INSTRUCTIONS` block in `orchestrator.md`'s
   `## SUB-AGENT SPAWN TEMPLATE`. The ordering is part of the cache key.
