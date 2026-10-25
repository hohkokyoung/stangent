# Benchmark

Two tools, run against the pinned repos in `repos.tsv` (D6, D28).

| Tool | What it measures | Clone it needs |
|---|---|---|
| `run.sh <label> <stangent args...>` | exit code, wall time, telemetry totals of one stangent command | shallow (it makes its own in `.cache/`) |
| `Stangent.Benchmark <clone> [options]` | scoring tests 1–4, OpenAPI check, expected-links key | **full history** and a built graph |

Results go in `results/<date>-<phase>-<repo>[-<part>].json` and are committed.

## Full-history clones for `Stangent.Benchmark`

`run.sh` clones are shallow (`--depth 1`), so the history tests (1–2) can't use them. Make full clones
outside the repo (a scratch folder), one per repo:

```bash
B=<scratch>/bench          # anywhere outside this repo
git clone --filter=blob:limit=200k https://github.com/Kareadita/Kavita.git $B/kavita
git -C $B/kavita checkout -q 2d225367f0310ac7354423b772a4c6a7caf63fd2   # the commit pinned in repos.tsv
```

`blob:limit=200k` downloads every small file's history but skips large files: Test 2's `git blame` and
📜's rename following need those contents. A blobless clone (`blob:none`) fetches them one at a time over
the network (hours for Test 2). To fix an existing blobless clone, fetch them in one go:

```bash
git -C $B/kavita config --unset remote.origin.partialclonefilter
git -C $B/kavita fetch -q --refetch origin
```

Then in each clone:

1. `stangent init`, then replace its `stangent.yaml` with `configs/<name>.yaml` when one exists (detector settings).
2. Copy `configs/<name>.global.json` to the clone's `global.json` when one exists. nopCommerce, Sonarr and
   CleanArchitecture pin SDKs this machine may lack. Without it the build fails with `sdk_missing`.
   The scoring tool also takes `--sdk-override configs/<name>.global.json` for the past commits it rebuilds.
3. `stangent graph build` once (restores packages; frontends need Node).

Build the CLI explicitly before using it from a copy. `dotnet test` doesn't rebuild `src/Stangent.Cli`:

```bash
dotnet build src/Stangent.Cli && cp -R src/Stangent.Cli/bin/Debug/net10.0 $B/stangent-bin
st() { dotnet $B/stangent-bin/stangent.dll "$@"; }        # zsh doesn't word-split $VAR, so use a function
```

## Running the scoring

```bash
dotnet build benchmark/Stangent.Benchmark
bench() { dotnet run --project benchmark/Stangent.Benchmark --no-build -- "$@"; }
bench $B/sonarr --out benchmark/results/<date>-<phase>-sonarr-history.json                 # tests 1–3 and Gate 1 (D44)
bench $B/nopcommerce --compile-break 40 --sdk-override benchmark/configs/nopcommerce.global.json --out ...
bench $B/kavita --only-frontend --openapi openapi.json --ts-compile-break 40 --out ...      # 1b frontend checks
bench $B/cleanarchitecture --only-frontend --expected benchmark/expected/cleanarchitecture-links.json --out ...
```

Options: `--months 0 (all history) --max-commits 200 --max-fixes 400 --exact-sample 0 --sdk-override <file> --skip-build-test
--compile-break <n> --only-compile-break --openapi <file> --ts-compile-break <n> --only-frontend --expected <file> --tune
--history-settings <w,ms,share,max> --out <file>`.

History is read on main's own line (`git log --first-parent`): a merged PR counts once (D36). Test 2
has one case per fix, however many changes blame points at (D33), and reports a `gate1` block: "graph
within 2 lines" with and without the co-change baseline's top 10, with 95% ranges from resampling fixes
(fixed seed), split into single-topic and bundle fixes.

**📜 (Phase 1c).** Every history run also scores 📜: the same counting `graph build` uses (`HistoryIndex`), but
for each past change only from commits older than it, so no fix leaks into its own prediction. `historySelfCheck`
confirms the index at today's commit reproduces the graph's stored lines. The `gate2` block is D44's Gate 2.
Tuning (D43) runs a grid on the tuning repos, then picks the defaults by one rule, in code:

```bash
bench $B/nopcommerce --skip-build-test --tune --out $B/nopcommerce-tune.json
bench $B/sonarr --skip-build-test --tune --out $B/sonarr-tune.json
bench --select-tuning $B/nopcommerce-tune.json $B/sonarr-tune.json --out benchmark/results/<date>-phase1c-tuning.json
```

`--history-settings window,minShared,minShare,maxFiles` scores other settings without editing `stangent.yaml`.

**The split check (Phase 1d, D45–D49).** Walks main-line commits newest first from the clone's graph commit.
It takes changes of ticket size (≥ 3 changed members across ≥ 2 files, ≤ 15 files) whose parent and change both
build, and splits their changed members with the graph:
- judged: within 2 steps, ✅ + 🟡;
- reported beside it: direct only, and within 2 steps + 📜;
- baseline: split by folder.

Each group is applied alone onto the parent. Step A builds it; step B runs the unit tests that pass on both the
parent and the change. The run stops at 30 split cases or 200 examined, and puts the clone back at its start
commit (an interrupted run is put back by the next one).

```bash
bench $B/sonarr --split --split-config benchmark/configs/sonarr.split.json --sdk-override benchmark/configs/sonarr.global.json \
  --out benchmark/results/<date>-phase1d-sonarr-split.json
bench $B/bitwarden --split --split-config benchmark/configs/bitwarden.split.json --out benchmark/results/<date>-phase1d-bitwarden-split.json
```

`configs/<name>.split.json` sets how the repo is built and unit-tested:
- `solution`;
- `testProjects`: a name pattern;
- `integrationMarkers`: test projects left out;
- `testFilter`: the repo's own unit-test categories;
- `buildExclude`: projects this machine can't build, left out with every project that references them;
- the frontend paths and the commands that typecheck them.

Runs take hours: each split case builds the parent, the change and every group, and runs the tests three or more times.
Each walked commit's result is saved in the clone's `.stangent/split-progress.jsonl`, so a crashed run resumes where it
stopped. `--groups-only <earlier results.json>` re-groups only the cases that run examined, building graphs but applying
nothing: a cheap way to compare a new split rule on the same cases.

The compile-break tests edit files in the clone and put them back. A run that was interrupted is
restored from git at the start of the next run.

Which repo proves what (D29, D32, D43): nopCommerce and eShop for C#; Kavita and Sonarr for frontends and
history (Kavita has a usable `openapi.json`); CleanArchitecture for the hand-checked links in `expected/`.
For 📜 history (Phase 1c): tuned on nopCommerce and Sonarr, judged on bitwarden/server (Kavita and eShop
are reported but have too few fixes to count).

## Phase 3: tickets on Sonarr (D77–D81)

The frozen set is in `tickets/sonarr/`: `selection.json` (40 bugs, newest first, and 10 not-bugs, chosen by
`--select-tickets`'s rule), `issues/<n>/issue.json` with the comments from before each fix (or closing) and their
attachments, and `answer-key.json` (fix commits and files: read by the scoring only, never by the run).

```bash
B=$HOME/stangent-bench                           # stays outside temp folders: the run spans days
git clone --filter=blob:limit=200k https://github.com/Sonarr/Sonarr.git $B/sonarr && git -C $B/sonarr checkout -q 0bffefe7c8b7804068ab0844af218376e58b1acb
dotnet build src/Stangent.Cli && cp -R src/Stangent.Cli/bin/Debug/net10.0 $B/stangent-bin
(cd $B/sonarr && dotnet $B/stangent-bin/stangent.dll init && cp $OLDPWD/benchmark/configs/sonarr.tickets.yaml stangent.yaml \
  && cp $OLDPWD/benchmark/configs/sonarr.global.json global.json && cp -R $OLDPWD/benchmark/results/2026-10-24-phase2-sonarr/specs . \
  && dotnet $B/stangent-bin/stangent.dll graph build)
benchmark/tickets/sonarr/app/setup.sh $B/sonarr      # Playwright app setting: build, start offline with seeded series
bench --select-tickets $B/sonarr --out-dir benchmark/tickets/sonarr   # done once; the result is committed
bench --copy-to-jira benchmark/tickets/sonarr --site peachpersonal.atlassian.net --project SONB [--dry-run]   # asked first
benchmark/tickets/run.sh $B/sonarr benchmark/tickets/sonarr points pages bundles drafts prove replay --model sonnet
bench --score-tickets $B/sonarr benchmark/tickets/sonarr --out benchmark/results/<date>-phase3-sonarr-score.json
```

- The run uses product defaults (AI picks on, `fsd.findings` off, the cap) and no developer edits or hints (D79).
- `prove` checks out each bug's fix parent (a not-bug's commit before it closed); a reproduction is run again on the fix
  commit with `ticket prove --verify` ("proof is real"). Each phase resumes; `--only KEY,...` limits it.
- Sonarr builds a single project only with `-p:SolutionDir=…/src/` (`tickets.dotnetArgs`), and serves the UI from
  `_output/net10.0/UI` (`app/build.sh` copies it). The seed writes two series straight into the database, so no
  metadata service is called.
