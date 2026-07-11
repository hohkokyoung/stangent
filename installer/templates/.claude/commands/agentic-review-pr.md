---
description: Fetch a GitHub PR via the github MCP and red-team it with the architect + security-reviewer agents. Optionally posts a summary comment.
argument-hint: "<PR number or URL> [--comment]"
---

# /agentic-review-pr

Review an existing GitHub pull request with the **architect** (design) and
**security-reviewer** (exploits) agents. The command owns all GitHub I/O; the
agents stay offline and read-only — they never call the github MCP themselves.

## Preconditions

The github MCP must be enabled and credentialed:
- `github` present in `enabledMcpjsonServers` in `.claude/settings.json`
- `GITHUB_PERSONAL_ACCESS_TOKEN` filled in `.mcp.json` (no `REPLACE_WITH_`)

If either is missing, print exactly what to fix and STOP — do not fall back to
guessing the diff from local git.

## Procedure

### Step 1 — Resolve the PR and allocate an id

Parse `$ARGUMENTS` for a PR number or URL and the optional `--comment` flag.

```bash
PR_ID=PR-$(date +%Y%m%d-%H%M%S)
mkdir -p .claude/state/pr-review/$PR_ID
```

### Step 2 — Fetch via github MCP (YOU do this — do NOT delegate)

Using the github MCP, fetch the PR's metadata (title, body, base/head refs,
changed files) and its unified diff. Save them locally so the agents read from
disk, not the network:

- `.claude/state/pr-review/<PR_ID>/pr.md` — title + body + base/head
- `.claude/state/pr-review/<PR_ID>/diff.patch` — the unified diff

The github MCP is called ONLY here, by you. Agents never touch it.

### Step 3 — Run the two reviewers

For each agent, arm the hook, invoke pointing at the saved files as `scope`,
then clear state. (`reviewer` is deliberately not used here — it is task-file
coupled and needs acceptance criteria a raw PR does not carry.)

**Architect:**
```bash
printf '%s' 'architect' > .claude/state/current_role.txt
```
Invoke **architect** with `review_id=<PR_ID>` and
`scope="GitHub PR: <title>; diff at .claude/state/pr-review/<PR_ID>/diff.patch, description at .../pr.md"`.
Wait for its findings, then:
```bash
rm -f .claude/state/current_role.txt
```

**Security-reviewer:** same handshake with role `security-reviewer` and the same
`scope`, writing to `.claude/state/security-review/<PR_ID>/findings.md`.

### Step 4 — Present, then optionally comment

Print both reports verbatim: the design verdict (`sound`/`concerns`/`reconsider`)
and the security verdict (`no-blockers`/`hardening-needed`/`exploitable`).

**If `--comment` was passed:** posting to a PR is outward-facing — show the exact
comment body and ask for confirmation before posting. The comment includes:
- the design findings (High/Medium) and their recommendations, and
- security findings as **counts + categories only** (e.g. "2 High: broken access
  control, injection") — never the exploit scenarios. Full exploit detail stays
  in the gitignored `.claude/state/` report so it does not land in PR history,
  which may be public.

Post via the github MCP only after the user confirms. Without `--comment`, do not
post anything.

## Constraints

- Only the command calls the github MCP. The agents never do — do not add
  github tools to their invocations.
- Do NOT post exploit scenarios to the PR. Counts and categories only.
- Do NOT auto-post; `--comment` + explicit confirmation is required.
- Do NOT edit code or the PR's files. This is review only.
