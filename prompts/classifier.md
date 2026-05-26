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
   - **Adding field sections or options to an existing form/dialog/overlay widget** (even
     if the widget is large and AC count is high — the change is still contained)
   - **Adding or fixing a loading, error, or empty state in an existing widget**

2. Likely touches **≤ 4 existing files** — no new files expected (a primary widget file +
   its test file + one provider/constants file still qualifies).

3. No database changes (no new migrations, no schema changes, no model additions).

4. No new API endpoints or changes to existing API contracts.

5. No new external dependencies.

6. No new environment variables.

7. No architectural decision required (nothing in decisions.json would be affected).

---

**Classify as `standard` when ANY of the following is true:**

- New screen, page, or major UI section
- New API endpoint or change to existing endpoint contract
- New service, repository, or provider class
- Database migration or model change
- New package/dependency added
- Feature spans more than one domain (e.g. auth + profile + analytics)
- Multiple acceptance criteria **across different stacks** (e.g. mobile UI + backend +
  database) — note: many AC items within a single stack does NOT trigger standard
- Unclear scope — requires developer clarification
- Estimated files to touch > 4, or new files expected

---

## Output

After applying the rules to `raw_request`, set:

```
tier = "direct"   ← all direct conditions met
tier = "standard" ← any standard condition met, or ambiguous
```

When in doubt: ask yourself "does this feature add new architectural structure
(new file, new class, new API shape, new dependency) or does it extend/fix
something that already exists?" Extending existing structure → `direct`.
Creating new structure → `standard`. A wrong `direct` misses one spec section
(low cost, caught in review). A wrong `standard` burns 40–60 k extra tokens
every run (high cost, invisible). Default to `direct` for pure UI work.

Write `tier` to the feature file frontmatter.
