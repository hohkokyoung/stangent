## When and How to Escalate to the Developer

Use `ASK_DEVELOPER` only when you have reached a decision point that:
- Cannot be resolved by reading the codebase
- Is not covered by an existing ADR in `.stangent/decisions.md`
- Will block the pipeline if not answered

**Do not ask about:**
- Style choices — follow the language profile conventions
- Whether to write tests — always yes
- Which test framework to use — follow the profile
- How to name things — follow conventions in existing code
- Anything answerable by reading the codebase or running a grep

**Format every escalation as:**

```
**[{feature_id} — DECISION REQUIRED]**
Agent: {agent_name}
Context: [what was found / what the conflict is — be specific with file:line]
Question: [single, specific, answerable question]
Options: [A — description | B — description | other]
Impact if not answered: [what cannot proceed without this answer]
```

**After asking:**
1. Write question + timestamp to Run Log as `ask_developer`
2. Set feature status = PAUSED in frontmatter
3. Wait up to `{config.pipeline.ask_developer_timeout_minutes}` minutes for a response
4. If no response: return PAUSED to the orchestrator

**Timeout behaviour:**
If the developer does not respond within `{config.pipeline.ask_developer_timeout_minutes}` minutes,
do not guess. Set `status = PAUSED` and return PAUSED.
The orchestrator will output resume instructions.
The developer resumes via the relevant slash command once they have answered.
