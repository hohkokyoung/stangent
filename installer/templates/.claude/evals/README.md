# Evals

Test cases that pin agent behavior. Run after every prompt edit so you can catch regressions before they reach a real feature.

## Layout

```
.claude/evals/
├── run.py
├── planner/
│   ├── case_01_minimal/
│   │   ├── input.md          # the user goal to feed /agentic-plan
│   │   ├── expect.md         # human-readable expectation
│   │   └── assert.py         # programmatic check
│   └── ...
├── implementer/
│   ├── case_01_status_lifecycle/     # status pending→done, Design+Decisions log filled
│   ├── case_02_blocked_missing_adr/  # blocked when ADR file is absent
│   └── ...
└── reviewer/
    ├── case_01_review_appended/      # ## Review filled, status left unchanged
    ├── case_02_blocking_review/      # blocks on SQL injection violation
    └── ...
```

Three files per case:

- **`input.md`** — setup instructions and the task file content (or goal text for planner cases).
- **`expect.md`** — what a human reading the output should see. Prose.
- **`assert.py`** — programmatic check. Exposes `check(run_dir: Path) -> list[str]` which returns a list of failure messages (empty = pass).

## Running

v1 is **score-only**: you run the agent manually, then score the output.

```bash
# Planner case
/agentic-plan <paste contents of input.md>
python .claude/evals/run.py planner/case_01_minimal FEAT-NNN

# Implementer / reviewer case — follow setup steps in input.md, then:
python .claude/evals/run.py implementer/case_01_status_lifecycle FEAT-901
python .claude/evals/run.py reviewer/case_01_review_appended FEAT-903
```

To score every case under a role at once (requires a `.runid` file in each case dir):

```bash
python .claude/evals/run.py planner --all
python .claude/evals/run.py implementer --all
python .claude/evals/run.py reviewer --all
```

## Adding a case

1. Copy an existing case dir as a template.
2. Edit `input.md` to the goal you want to pin.
3. Edit `expect.md` with the human-readable expectation.
4. Edit `assert.py`. Use the helpers in `run.py` (parsed frontmatter, task graph, etc.).

## Asserter API

`assert.py` must define:

```python
from pathlib import Path

def check(run_dir: Path) -> list[str]:
    """Return [] on pass, or a list of human-readable failure messages."""
    ...
```

`run.py` provides parsed inputs as helpers — see the cases for examples.

## Philosophy

- **Structural assertions only in v1.** Frontmatter fields, task counts, role distribution, dep-graph validity, AskUserQuestion was/wasn't called. Don't grade prose semantics yet — that needs an LLM-as-judge harness, which is its own project.
- **One symptom per case.** Cases are not integration tests. If a planner change regresses one case, you should know *which* axis broke.
- **Failing assertions name the bullet.** Return concrete messages: `"expected ≥4 tasks, got 2"` not `"task count wrong"`.
