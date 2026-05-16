# meta.md — Cascade Rules

Place this file at `.stangent/meta.md` in your project.

The planner reads it automatically before writing every spec.
When a feature touches a file that matches a trigger pattern,
the planner adds the dependent files to `## Files to Touch`
so they are reviewed and updated as part of the feature.

---

## Format

Each row is a cascade rule: "when you touch X, also review Y".

| When you touch | Also review |
|----------------|-------------|
| src/models/*.py | docs/api.md |
| src/routes/*.py | README.md, docs/api.md |
| pubspec.yaml | CHANGELOG.md |
| lib/core/theme.dart | docs/design_system.md |

**Trigger (left column):** a glob pattern relative to the project root.
**Dependent (right column):** comma-separated paths to add to `## Files to Touch`.

The planner adds dependent files with a note: `[meta cascade from {trigger}]`

---

## When to use meta.md

- Your API docs need to stay in sync with route changes
- A design system file must be reviewed whenever theme tokens change
- A CHANGELOG must be updated when public-facing features complete
- A shared config file requires downstream doc updates

---

## Example — Python API project

```markdown
| When you touch | Also review |
|----------------|-------------|
| src/models/*.py | docs/data_models.md |
| src/api/routes/*.py | docs/api_reference.md, README.md |
| src/config/*.py | .env.example |
| requirements*.txt | docs/setup.md |
```

## Example — Flutter app

```markdown
| When you touch | Also review |
|----------------|-------------|
| lib/core/theme.dart | docs/design_system.md |
| lib/core/router.dart | docs/navigation.md |
| pubspec.yaml | CHANGELOG.md, docs/setup.md |
| lib/l10n/*.arb | docs/localisation.md |
```

---

## Notes

- The planner uses `## Files to Touch` as a best-guess, not a hard lock.
  The gateway enforces the actual allowed paths from the contract.
- If a dependent file is out of scope for the feature, the developer can
  remove it during the AWAITING_CONFIRMATION review.
- Keep rules specific — broad patterns (e.g. `**/*.py → docs/`) generate
  noisy plans and slow down the planner.
