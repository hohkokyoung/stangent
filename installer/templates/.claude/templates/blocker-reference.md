# Blocker Reference

All valid `blocker:` values by role. Format: always a quoted string. Set only when `status: blocked`.

## Format rules

- Use the exact codes below — do not invent variants.
- Angle-bracket placeholders (`<id>`, `<summary>`) must be filled with the real value.
- `blocker: null` when status is NOT blocked or deferred.
- `resume_when:` is set only alongside `status: deferred`; null otherwise.

---

## Codes by role

### All roles
| Code | When to use |
|------|-------------|
| `"missing_adr: <id>"` | A listed ADR file is absent or has `status != accepted`. |
| `"context_budget_exceeded"` | `system + role + ADRs + skills + task` already exceeds model window before any retrieval. |

### Implementer
| Code | When to use |
|------|-------------|
| `"insufficient_context"` | Two retrieve calls exhausted and context is still insufficient to implement safely. |
| `"<failing DoD bullet>"` | A specific `definition_of_done` bullet cannot be satisfied. Use the exact bullet text. |

### Reviewer
| Code | When to use |
|------|-------------|
| `"review: <short reason>"` | Verdict is `blocking`. Reason should name the specific ADR violation, security smell, or correctness failure. |

### Tester
| Code | When to use |
|------|-------------|
| `"<failing test or DoD bullet>"` | A specific test case or DoD bullet failed. Use the exact test name or bullet text. |

### Refactor
| Code | When to use |
|------|-------------|
| `"pre-existing test failures: <summary>"` | Test suite was already failing before any changes. Include pass/fail counts. |
| `"regression: <failing test summary>"` | Tests that previously passed now fail after the refactoring. Include test names. |
| `"new behavior detected: <description>"` | Refactoring would require adding functionality beyond pure restructuring. |

### Planner (update mode)
| Code | When to use |
|------|-------------|
| `"superseded by t<N>"` | Task is replaced by a newer task in an update. |
| `"open question: <ref>"` | A genuine ambiguity not covered by the Clarifications block blocks this task. Reference the open question in `_overview.md`. |

### Sketcher
| Code | When to use |
|------|-------------|
| `"preview_mcp_unavailable"` | The preview MCP tool did not respond or is not configured. |

### Deferral — set by `/agentic-defer` only, NEVER by an agent
| Code | When to use |
|------|-------------|
| `"external: <dependency>"` | The world isn't ready: undeployed backend, missing credentials, a third party or another team. Set on every non-`done` task by `/agentic-defer`, together with `status: deferred` and a `resume_when:` condition. An agent that cannot proceed for an in-run reason uses its role's codes above — agents MUST NOT emit this code or the `deferred` status. |

---

## Recovery steps

| Code | How to resolve |
|------|----------------|
| `missing_adr` | Add the ADR file with `status: accepted`, or remove the id from `task.adrs`. |
| `context_budget_exceeded` | Reduce `skills_to_load`, split the task, or use a larger-context model. |
| `insufficient_context` | Add a relevant skill to `skills_to_load`, increase `k`, or add reference docs to the skill. |
| `pre-existing test failures` | Fix failing tests first, then re-run the refactor. |
| `regression` | Revert the refactoring change that caused the regression; re-approach that specific change. |
| `review: *` | Address the reviewer's blocking finding, then re-run `/agentic-build <reviewer-task-id>`. |
| `external: *` | Clear the external dependency, then run `/agentic-resume <run-id>` — never hand-flip `deferred` back to `pending`. |
