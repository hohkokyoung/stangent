# The planted Sonarr FSD (Phase 2b, D56, D57)

- `sonarr-fsd.outline.md` → `sonarr-fsd.docx` (`Stangent.Benchmark --fsd-docx`): an FSD for 15 Sonarr screens, written
  in our own words from the Servarr wiki (github.com/Servarr/Wiki, `sonarr/*.md`). Some chapters use Word's automatic
  numbering (1, 1.1), one has typed numbers (3, 3.1), the rest are plain headings (ids are heading paths).
- `answer-key.json`: 30 planted items, 10 per kind, each checked against Sonarr's code at the pinned commit:
  - **gaps:** features Sonarr doesn't have, described as if it did;
  - **conflicts:** real rules stated wrongly (`code` says what the code does);
  - **undocumented:** 3 real screens and 7 real behaviours left out of the FSD.
  The key also holds the frozen matching rule and the FSD's scope (`fsd.scope`): the areas the FSD's introduction
  names, as route prefixes, not a list of screens (which would point code at the 3 planted screens; changed after a
  fresh review, before the first judging run). **It never goes into a bundle:** no
  stangent command reads it; only the scoring command does.
- Both were committed before the first judging run (2026-10-20) and are frozen: a change is a new benchmark, reported as such.

The clone's `stangent.yaml` needs `fsd: { findings: true, archive: …, scope: <the key's scope> }`: findings are
off by default (D66), and the benchmark measures them. The pages scored are in `page-set.json` (`benchmark/sonarr-page-set.py`).

Scoring: `dotnet run --project benchmark/Stangent.Benchmark -- --score-fsd <sonarr clone> benchmark/fsd/sonarr/answer-key.json [--hand-check <file>] --out <file>`.
