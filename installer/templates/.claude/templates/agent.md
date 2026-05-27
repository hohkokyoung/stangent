---
name: <agent-name>
description: <one-sentence statement of what this agent does — single verb if possible. The dispatcher uses this to route.>
tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Bash
  # add MCP tools only if this agent needs them:
  # - mcp__agentic_mcp__retrieve
  # - mcp__dbhub
  # - mcp__supabase
---

# <Agent Name> Agent

You <single-verb description, e.g. "implement one task", "review one task", "decompose a goal into tasks">. You are given <input>.

## Injection order (you are loaded after these)

```
1. system prompt
2. this role prompt
3. ADRs from task.adrs (verbatim, accepted only)     [if applicable]
4. skills from task.skills_to_load (verbatim)        [if applicable]
5. retrieved reference chunks (one retrieve call)    [if applicable]
6. the task file itself                              [if applicable]
```

**Conflict precedence:** system > role > ADRs > skills > retrieved context > model reasoning.

## Write-scope rules (HARD)

<List exactly which sections / files / frontmatter fields this agent may write.
Be strict. Every other agent has a strict scope and so should this one.>

- May write: <section / field>
- May NEVER write: <section / field>

## Procedure

<Numbered steps. Each step is one action. The agent should be able to follow this top-to-bottom without branching.>

1. <step>
2. <step>
3. ...

## MCP rules

<Specify which MCP tools this agent may call and in what context.
If this is a non-runtime agent (planner, reviewer), MUST NOT use external MCP.>

- `mcp__agentic_mcp__retrieve`: exactly one call. [or: not allowed]
- `mcp__dbhub` / `mcp__supabase`: allowed for <runtime purpose>. [or: not allowed]

## Stop condition

<When does this agent terminate its turn? Be specific. "After flipping status to done | blocked", "After writing files", etc.>

---

## Authoring rules

- **Single responsibility.** If you find yourself writing a second top-level "Procedure", split into two agents.
- **No language knowledge.** References to fastapi / flutter / supabase belong in skills, not in agent prompts.
- **No state-machine logic.** State lives in task files; the dispatcher routes.
- **Soft size ceiling: 200 lines.** Past that, audit for hidden responsibilities.
- **Register the agent.** Update any dispatcher / command that should be able to invoke this agent.
