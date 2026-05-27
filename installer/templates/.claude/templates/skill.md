# SKILL: <name>

## Purpose
<One-paragraph scope statement. What does this skill govern? What does it explicitly NOT cover? The planner uses this section to verify non-overlap when assigning skills to a task.>

## Rules
<Hard behavioral rules. Numbered. Each rule should be:
  - one sentence
  - actionable (the implementer can apply it)
  - testable (the reviewer can check it)>

1. <rule>
2. <rule>

## Patterns
<Common implementation patterns. Code snippets welcome. Keep snippets short and idiomatic.>

- **<pattern name>:**
  ```<lang>
  // example
  ```

## Anti-patterns
<What this skill forbids. Reviewer checks for these.>

- <anti-pattern>
- <anti-pattern>

---

## Authoring rules

- Total length ≤ 3000 tokens. If a topic needs more, split into multiple skills.
- Non-overlapping with other skills by Purpose. Two skills with overlapping Purpose sections is a planner-validation failure.
- Don't reference specific files / classes / functions from any project — skills are project-agnostic. Project-specific decisions live in ADRs.
- Place the file at `.claude/skills/<name>/SKILL.md`. Reference docs go in `.claude/skills/<name>/references/*.md`.
