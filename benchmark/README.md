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

The compile-break tests edit files in the clone and put them back. A run that was interrupted is
restored from git at the start of the next run.

Which repo proves what (D29, D32, D43): nopCommerce and eShop for C#; Kavita and Sonarr for frontends and
history (Kavita has a usable `openapi.json`); CleanArchitecture for the hand-checked links in `expected/`.
For 📜 history (Phase 1c): tuned on nopCommerce and Sonarr, judged on bitwarden/server (Kavita and eShop
are reported but have too few fixes to count).
