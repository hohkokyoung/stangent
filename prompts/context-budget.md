## Context Budget

All pipeline agents must track how much of the codebase they have read and degrade gracefully when approaching limits.

### Budget

Read `config.pipeline.agent_context_budget_chars` from `.stangent/config.json`.
Default: `300000` characters (≈ 75k tokens of codebase content).

### Tracking

Maintain a running total `chars_read` as you read files. Approximate: `chars_read += len(file_content)`.

### Thresholds

**At 80% of budget (`chars_read >= 0.8 * budget`):**
- Log to Run Log: `CONTEXT_BUDGET_WARNING — {chars_read} / {budget} chars used. Switching to grep-only mode.`
- Switch to grep-only mode: no more full-file reads. Use grep/search to locate specific lines or patterns, then read only the matched sections (±10 lines context).

**At 100% of budget (`chars_read >= budget`):**
- Log to Run Log: `CONTEXT_BUDGET_EXCEEDED — stopping codebase scan.`
- Write a `## Context Checkpoint` block to the feature file:
  ```
  ## Context Checkpoint
  Budget exhausted at {chars_read} chars. Partial scan complete.
  Last file read: {path}
  Remaining anchor files not read: {list}
  ```
- Return current accumulated state to the orchestrator. Do not attempt further file reads.

---

## context_cache.md Format

`.stangent/context_cache.md` is a shared codebase snapshot. The planner writes it; all other agents read it to avoid redundant scanning.

**Format:**

```
---
git_hash: <output of: git rev-parse HEAD>
created_at: <ISO 8601 timestamp>
---

## Tree Structure
<depth-3 directory tree, one path per line, exclude_dirs already filtered>

## Anchor Summaries
### path/to/anchor_file.py  (N lines)
<first 80 lines of the file>
---
### path/to/another_file.ts  (N lines)
<first 80 lines of the file>
---
```

**Rules:**

- `git_hash` is the full commit hash at the time the cache was written.
- Each `## Anchor Summaries` entry is separated by `---` on its own line.
- Files over 300 lines: include only the first 80 lines. Do not truncate shorter files.
- Maximum 20 anchor file entries per cache. If more anchor files exist, include the top 20 by profile priority.

**Who reads/writes it:**

| Agent | Action |
|-------|--------|
| Planner | Always writes after Pass 1. If cache exists and `git_hash` matches current HEAD, skip re-scan and use existing cache. |
| Implementer | Read before Pass 2. If `git_hash` matches HEAD, skip Pass 1 + Pass 2 entirely. |
| Reviewer | Read at start. If `git_hash` matches HEAD, skip Pass 1 + Pass 2. |
| SRS Agent | Read at start. If `git_hash` matches HEAD, skip Pass 1 + Pass 2. |

**Staleness check:**

```
current_hash = $(git rev-parse HEAD)
cache_hash   = front matter git_hash field
stale        = (current_hash != cache_hash)
```

If stale: proceed with normal Pass 1 + Pass 2, but do NOT rewrite the cache (only the planner may write it).

**Cache miss behaviour:** If `context_cache.md` does not exist, proceed with normal Pass 1 + Pass 2.
