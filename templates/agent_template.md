# Stangent Agent Template
> Canonical format every agent in this framework must follow.
> Version this document when the format changes.

---

## File Location Rules

| Type | Location | Copied to project? |
|------|----------|--------------------|
| `command` | `commands/<name>.md` | Yes → `.claude/commands/` |
| `agent` | `agents/<name>.md` | No — read by commands at runtime |
| `subagent` | `agents/subagents/<name>.md` | No — read by agents at runtime |
| `profile` | `profiles/<name>.md` | No — read by agents at runtime |

---

## Frontmatter Schema

Every agent file opens with this YAML frontmatter block. All fields are required
unless marked optional.

```yaml
---
name: <snake_case>
version: <semver>            # bump patch=wording, minor=new steps, major=breaking I/O
type: command | agent | subagent
description: >
  One sentence. Used by orchestrator for routing. Be specific.
tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Bash
  - Agent                    # only if this agent spawns subagents
  - WebSearch                # only if external lookup needed
inputs:
  - name: <param>
    type: string | path | json
    description: <what it contains>
outputs:
  - name: <param>
    type: string | path | json
    description: <what it contains>
profile_aware: true | false   # reads config.json and loads language profile
allows_ask_developer: true | false
bash_allowlist:               # explicit permit list — omit = no Bash permitted
  - "ruff check"
  - "pytest"
  - "git diff"
  - "git add"
  - "git commit"
  - "git checkout -b"
bash_blocklist:               # always blocked regardless of allowlist
  - "git reset"
  - "git push --force"
  - "rm -rf"
  - "git clean"
  - "git checkout --"
  - "drop table"
  - "DELETE FROM"
---
```

---

## System Prompt Structure

Every agent prompt body follows this exact section order. Include all sections.
Omit a section only if genuinely not applicable — mark it `N/A` rather than deleting.

### 1. ROLE
One sentence. What this agent is and what it owns.

```
You are the Stangent [NAME] agent. You are responsible for [OWNED RESPONSIBILITY].
```

### 2. CONTEXT INPUTS
What to read before taking any action. Listed in read order.

```
Before doing anything:
1. Read config.json — load profile name and paths
2. Read profiles/{{profile}}.md — load language-specific rules
3. Read .stangent/decisions.md — load all ADRs
4. Read the feature file at {{feature_file_path}}
5. [any additional reads]
```

### 3. CONSTRAINTS
Hard rules. No exceptions. Numbered list.

```
CONSTRAINTS — these are absolute, never negotiated:
1. Never write to a section you do not own in the feature file
2. Never proceed past an ambiguity — use ASK_DEVELOPER
3. Never run a Bash command not in your allowlist
4. [language/role-specific constraints]
```

### 4. OUT OF BOUNDS
What this agent must never do, even if asked.

```
OUT OF BOUNDS for this agent:
- Do not modify files outside the project src_root unless explicitly listed in ## Files to Touch
- Do not install packages or modify lockfiles
- Do not push to remote
- [role-specific]
```

### 5. PROCESS
Numbered steps. Each step is atomic and verifiable.

```
PROCESS:
1. [step]
2. [step]
   a. [sub-step]
   b. [sub-step]
3. [step]
```

### 6. OUTPUT CONTRACT
Exactly what to write, where, and in what format.

```
OUTPUT:
- Write ## [OWNED SECTION] in {{feature_file_path}}
- Append one JSON line to .stangent/logs/{{feature_id}}.jsonl per action
- Return: [what to return to calling agent]
```

### 7. ESCALATION
When and how to pause and ask the developer.

```
ESCALATION — use ASK_DEVELOPER when:
- [specific trigger 1]
- [specific trigger 2]

Format:
**[{{feature_id}} — DECISION REQUIRED]**
Agent: {{name}}
Context: [what you found]
Question: [specific, single answerable question]
Options: [A | B | other]
Impact if not answered: [what blocks]

After asking: set feature status = PAUSED. Stop all work.
Timeout: 30 minutes. If no response, write status = PAUSED to feature file
and output: "Pipeline paused. Resume with /implement {{feature_id}}"
```

---

## Run Log Entry Format

One JSON line per significant action. Written to `.stangent/logs/FEAT-XXX.jsonl`.
Never pretty-print — one line per entry.

```json
{"ts":"2026-05-09T10:00:00Z","feat":"FEAT-001","agent":"planner","version":"1.0.0","action":"started","detail":"raw request received","tokens_in":0,"tokens_out":0,"result":"ok"}
{"ts":"2026-05-09T10:00:05Z","feat":"FEAT-001","agent":"planner","version":"1.0.0","action":"read_file","detail":"lib/main.dart","tokens_in":450,"tokens_out":0,"result":"ok"}
{"ts":"2026-05-09T10:01:00Z","feat":"FEAT-001","agent":"planner","version":"1.0.0","action":"ask_developer","detail":"JWT vs session auth?","tokens_in":0,"tokens_out":0,"result":"blocked"}
```

**Required action names:**
`started` | `read_file` | `write_file` | `bash_run` | `spawn_subagent` |
`ask_developer` | `developer_responded` | `stage_complete` | `retry` |
`escalated` | `paused` | `completed` | `failed`

---

## Feature File Ownership Map

| Section | Owner | Others may |
|---------|-------|------------|
| Scope, AC, Out of Bounds, Depends On, Files to Touch, ADRs Applied | planner | read |
| Pre-Implementation Scan, Implementation Log, Files Changed, Future Considerations | implementer | read |
| Linter Report | linter subagent | read |
| Test Report | unit_tester subagent | read |
| Query Analysis Report | query_analyzer subagent | read |
| Scope Verdict, Review Checklist, Review Verdict | reviewer | read |
| Security Report | security_scanner subagent | read |
| SRS Reference | srs_agent | read |
| status, branch, retry_count, agent versions | pipeline (orchestrator) | read |

**Rule:** If you did not create a section, you may not overwrite it.
Append to it only if the section explicitly states appending is allowed.

---

## Codebase Reading — 3-Pass Strategy

All agents that need codebase context must follow this strategy. Never read
everything at once.

```
Pass 1 — Tree scan (always, ≤10 tokens per file entry)
  Glob the src_root to depth 3. Exclude dirs from profile exclude_dirs list.
  Write/update .stangent/context_cache.md with: tree hash + timestamp + tree.
  Cache is valid if current tree hash matches cached hash.

Pass 2 — Anchor files (always)
  Read every file listed in profile anchor_files that exists.
  These give the project's structure, conventions, and entry points.

Pass 3 — Targeted reads (per feature, guided by spec)
  Read only files listed in ## Files to Touch + files discovered in Pass 2
  that are directly relevant to the feature scope.
  Log every file read in Run Log.
```

---

## Bash Safety Rules

1. Check every command against your `bash_allowlist` before running.
2. If a command is in `bash_blocklist`, refuse and log `action: "bash_blocked"`.
3. Log every bash execution: command, exit code, stdout length.
4. If exit code ≠ 0: do not silently continue. Log failure, assess impact.
5. Never run commands with user-supplied strings interpolated into them
   (command injection risk).

---

## ASK_DEVELOPER — Trigger Criteria

Use it. Do not assume. The cost of asking is low; the cost of a wrong assumption is a failed review and a retry.

**Always ask for:**
- Architectural choices not covered by any ADR
- Conflicts between the spec's Out of Bounds and what existing code requires
- Ambiguity in an acceptance criterion that would change what files are touched
- Discovery of existing code that does 80% of the feature — confirm before reusing

**Never ask for:**
- Things answerable by reading the codebase
- Things already decided in decisions.md
- Style preferences (follow the profile conventions)
- Whether to add tests (always yes)

---

## Versioning Rules

| Change type | Version bump | Example |
|-------------|-------------|---------|
| Prompt wording, clarity | patch | 1.0.0 → 1.0.1 |
| New process step, new output field | minor | 1.0.1 → 1.1.0 |
| Changed input/output contract | major | 1.1.0 → 2.0.0 |

Update the version in frontmatter. The run log records which version ran.

---

## Agent Tool — Spawning Protocol

This is the standard way any agent spawns a sub-agent or child agent via the
Agent tool. Follow this exactly so every caller passes the same shape of data
and every agent knows what to expect.

### Spawn format

When using the Agent tool, write the prompt in this structure:

```
INPUTS:
{
  "feature_id":        "FEAT-001",
  "feature_file_path": "/absolute/path/to/.stangent/features/FEAT-001-login.md",
  "stangent_path":     "/absolute/path/to/stangent",
  "config_path":       "/absolute/path/to/project/config.json",
  "extra": {
    "key": "value"     (any additional context the sub-agent needs)
  }
}

INSTRUCTIONS:
Derive project_root = Path(config_path).parent.parent
Read the full contents of: {project_root}/.claude/agents/subagents/stangent-{name}.md
(or {project_root}/.claude/agents/stangent-{slug}.md for main agents)
Then execute those instructions exactly using the inputs above.
```

### Why this shape

- `feature_id` — so the sub-agent can locate the feature file and log dir
- `feature_file_path` — absolute path avoids working-directory ambiguity
- `stangent_path` — sub-agent can load its own profiles and templates
- `config_path` — sub-agent reads config without assuming project root;
  also used to derive project_root for locating other agent files
- `extra` — open-ended bag for caller-specific context (e.g. `previous_verdict`,
  `files_changed`) without requiring a schema change for each new agent

### What sub-agents must do on receipt

1. Parse the INPUTS JSON block at the top of their prompt
2. Read the config file at `config_path`
3. Proceed with their CONTEXT INPUTS section using the resolved paths

### What callers must never do

- Pass a relative path for `feature_file_path` or `stangent_path`
- Omit `feature_id` (sub-agents write to logs keyed on this)
- Inline large file contents into the spawn prompt — pass a path, let
  the sub-agent read it (avoids double context load)

---

## Profile Loading

If `profile_aware: true`, the agent must:

1. Read `config.json` at `config_path` → get `profile` field
2. If `profiles` is an array (monorepo): identify which profile applies to
   the files being touched based on path prefix matching
3. Load `{stangent_path}/profiles/{profile}.md`
4. Use profile values for all tool commands, checklist items, and patterns

---

## Adding a New Language Profile

1. Copy `profiles/_base.md` → `profiles/<language>.md`
2. Fill in all required fields (see `templates/profile_guide.md`)
3. Add profile detection rule to `init.py` `detect_profile()`
4. Add eval test cases under `evals/<language>/`
5. Bump `profiles/_base.md` version if the contract changed
