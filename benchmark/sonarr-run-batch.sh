#!/usr/bin/env bash
# usage: run-batch.sh <ids.json> <from> <to> <tag>
S=/private/tmp/claude-501/-Users-hohkokyoung-Projects-stangent--claude-worktrees-phase2-spec-39a79e/87ede6f0-9b2b-4afd-ad60-f3365d39aa03/scratchpad/bench
cd $S/sonarr; export STANGENT=$S/stangent-bin/stangent
IDS=(); while IFS= read -r l; do IDS+=("$l"); done < <(jq -r ".[$2:$3][]" "$1")
date +%s > $S/$4.start
/Users/hohkokyoung/Projects/stangent/.claude/worktrees/phase2-spec-39a79e/plugin/scripts/spec-run.sh --jobs 3 --model sonnet --retry-failed "${IDS[@]}" > $S/$4.tsv 2> $S/$4.err
date +%s > $S/$4.end
