# Tier Classifier

Classify an incoming request as `direct` or `standard` using the rules below.
Run this inline — do not spawn a sub-agent.

---

## Classification Rules

**Classify as `direct` when ALL of the following are true:**

1. The request describes ONE of:
   - A visual fix (layout, spacing, colour, animation, text)
   - A bug fix with an obvious single cause
   - A copy or label change
   - A config value or flag change
   - A small behavioural tweak on an existing screen/endpoint
   - Removing or disabling something

2. Likely touches **≤ 3 existing files** — no new files expected.

3. No database changes (no new migrations, no schema changes, no model additions).

4. No new API endpoints or changes to existing API contracts.

5. No new external dependencies.

6. No new environment variables.

7. No architectural decision required (nothing in decisions.md would be affected).

---

**Classify as `standard` when ANY of the following is true:**

- New screen, page, or major UI section
- New API endpoint or change to existing endpoint contract
- New service, repository, or provider class
- Database migration or model change
- New package/dependency added
- Feature spans more than one domain (e.g. auth + profile + analytics)
- Multiple acceptance criteria across different system layers
- Unclear scope — requires developer clarification
- Estimated files to touch > 3, or new files expected

---

## Output

After applying the rules to `raw_request`, set:

```
tier = "direct"   ← all direct conditions met
tier = "standard" ← any standard condition met, or ambiguous
```

When in doubt, classify as `standard`. The cost of a wrong `direct` is a
missing spec section. The cost of a wrong `standard` is extra tokens.
Prefer safety.

Write `tier` to the feature file frontmatter.
Append to Pipeline History: `tier classified: {tier} — {one-line reason}`
