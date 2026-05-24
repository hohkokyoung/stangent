Show token/context usage breakdown for a feature run.

Usage:
  /stats             — stats for the current active feature
  /stats FEAT-XXX    — stats for a specific feature

Reads the observer run log and prints a breakdown of what burned context:
which files were read, how many chars each, grouped by agent/state.

---

## Step 1 — Resolve feature ID

If `$ARGUMENTS` is non-empty:
  Use `$ARGUMENTS` as feature_id (normalise: uppercase, add FEAT- prefix if missing).
Else:
  Try in this order:
    1. Read `.stangent/gateway/active.json` — use its `feature_id` if present.
    2. Read `config.paths.registry_path` — pick the most recently `updated`
       feature whose status is not COMPLETE/ABANDONED. If none, pick the
       most recently `updated` feature regardless of status.
    3. If still nothing: output "No active or recent feature. Pass a feature ID." and stop.

## Step 2 — Load config

Read `.stangent/config.json`.
Extract `log_dir = config.paths.log_dir`.

## Step 3 — Read run log

Read `{log_dir}/{feature_id}.jsonl`.
If it does not exist: output "No run log found for {feature_id}. Has the feature been planned yet?" and stop.

Parse each line as JSON. Collect all entries.

## Step 4 — Compute breakdown

Group entries by `agent`:

For each group:
  - Count `file_read` actions → `reads`
  - Sum `chars` across all `file_read` entries → `total_chars`
  - Collect the top 5 largest reads (by `chars`), sorted descending
  - Count `bash_run` actions → `bash_count`
  - Count `glob` + `grep` actions → `search_count`
  - Count `file_write` actions → `writes`

Grand totals:
  - `total_reads`  = sum of all reads across agents
  - `grand_chars`  = sum of all chars across agents
  - `total_writes` = sum of all writes across agents

## Step 5 — Output

Print:

```
Stats for {feature_id}  (tier: {tier from frontmatter or 'unknown'})
══════════════════════════════════════════════

{for each agent group, sorted by total_chars descending}
Agent: {agent}  ({state at last entry})
  Reads:       {reads}   ({total_chars} chars ≈ {total_chars // 4}k tokens)
  Top files:
    {path}  —  {chars} chars
    ...
  Bash runs:   {bash_count}
  Searches:    {search_count}
  Writes:      {writes}

──────────────────────────────────────────────
TOTAL  reads={total_reads}  chars={grand_chars}  writes={total_writes}
```

## Step 5b — Token & cost estimate

Read `.stangent/config.json` → `provider.name` and `models.{agent}`.

For each agent group:
  - input_tokens_estimate  = total_chars / 4  (rough)
  - output_tokens_estimate = writes * 500     (typical write size)
  - model = config.models.{agent} (use _direct variant if tier=direct)

Apply per-model pricing (USD per 1M tokens — only Anthropic models hardcoded;
warn for others):

  | Model | Input $/1M | Output $/1M |
  |---|---|---|
  | claude-opus-4-7         | 15.00 | 75.00 |
  | claude-sonnet-4-6       |  3.00 | 15.00 |
  | claude-haiku-4-5*       |  0.80 |  4.00 |
  | (other / unknown)       |   —   |   —   |

  cost_input  = (input_tokens_estimate  / 1_000_000) * input_rate
  cost_output = (output_tokens_estimate / 1_000_000) * output_rate

Print summary:
```
Cost estimate (Anthropic pricing, observer chars only — actual is higher):
  {agent} ({model}): ~${input_cost:.4f} in + ~${output_cost:.4f} out
  ...
  TOTAL estimate: ~${grand_total:.4f}

Note: this counts only what the observer logged. Real cost includes:
  - Agent prompt tokens (~5-10k per spawn, not logged)
  - Extended thinking tokens (Opus high mode adds 2-3x)
  - Orchestration overhead between agents
Multiply by 2-4x for a realistic estimate.
```

If grand_chars > 200000:
  Append: "⚠️  High context load. Consider Direct tier next time if the change was small."

STOP.
