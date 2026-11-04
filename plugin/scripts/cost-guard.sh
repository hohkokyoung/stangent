# The hard stop (T.2, D126), sourced by spec-run.sh and ticket-run.sh; benchmark/tickets/run.sh shares its ledger through
# STANGENT_COST_DIR and STANGENT_MAX_COST.
#
# What it can see: each call's `total_cost_usd` (Claude Code's own figure, dollars at API prices, also on a plan, where
# it's a measure and not a bill), any `rate_limit_event` a call streams (status, window, how much is used, when it
# resets; often only once a window nears its limit), and a failed call's message.
# What it can't see: the plan's usage card, or what's left before a call. So a plan limit is found when a call says so,
# or by --stop-at-usage once a call has reported a figure; it can't be foretold from nothing.
# Claude Code checks --max-budget-usd between turns, so one long answer can run past it. A stop is before the next call.
#
# Exit codes after guard_finish: 2 no cap chosen (nothing spent); 5 stopped (cap or usage limit, resume with the same
# command); 1 a step was refused and the batch went on (I.10); 125 the batch was stopped by anything else.

GUARD_LIMIT_WORDS='usage limit|limit reached|limit exceeded|hit your( [a-z-]+)* limit|reached your( [a-z-]+)* limit|out of( extra)? usage|rate.?limit|weekly limit|session limit|quota'

guard_init() {   # <--max-cost value, may be empty> <--stop-at-usage value, may be empty> <jobs> <command as typed>
  COST_CAP="${1:-${STANGENT_MAX_COST:-}}"
  STOP_AT_USAGE="${2:-${STANGENT_STOP_AT_USAGE:-}}"
  GUARD_JOBS="$3"
  GUARD_CMD="$4"
  if [ -n "$COST_CAP" ] && [ "$COST_CAP" != none ] && ! guard_is_amount "$COST_CAP"; then
    echo "--max-cost takes dollars above 0, or none" >&2; exit 2
  fi
  if [ -n "$STOP_AT_USAGE" ] && ! guard_is_amount "$STOP_AT_USAGE"; then
    echo "--stop-at-usage takes a percent above 0" >&2; exit 2
  fi
  GUARD_OWN=""
  if [ -z "${STANGENT_COST_DIR:-}" ]; then STANGENT_COST_DIR="$(mktemp -d)"; GUARD_OWN=1; fi
  mkdir -p "$STANGENT_COST_DIR/held"
  : >>"$STANGENT_COST_DIR/spent"
  GUARD_REFUSED="$(mktemp)"
  export COST_CAP STOP_AT_USAGE GUARD_JOBS GUARD_CMD STANGENT_COST_DIR GUARD_REFUSED GUARD_LIMIT_WORDS
  trap guard_cleanup EXIT
}

guard_is_amount() { awk -v v="$1" 'BEGIN { exit !(v ~ /^[0-9]*\.?[0-9]+$/ && v + 0 > 0) }'; }

guard_cleanup() {
  rm -f "${GUARD_REFUSED:-}"
  if [ -n "${GUARD_OWN:-}" ]; then rm -rf "${STANGENT_COST_DIR:-}"; fi
}

guard_lock() { until mkdir "$STANGENT_COST_DIR/lock" 2>/dev/null; do sleep 0.05; done; }
guard_unlock() { rmdir "$STANGENT_COST_DIR/lock" 2>/dev/null || true; }

# The first reason stands: jobs still running stop at their next call with the same word.
guard_stop() { [ -f "$STANGENT_COST_DIR/stopped" ] || printf '%s\t%s\n' "$1" "$2" >"$STANGENT_COST_DIR/stopped"; }

guard_stopped_line() { printf 'stopped\t%s\t%s\n' "$1" "$(cut -f1 "$STANGENT_COST_DIR/stopped" 2>/dev/null)"; }

# An estimate from this repo's own earlier calls (`stangent cost estimate`); none yet means "unknown", said plainly.
guard_estimate() {   # <the check command that records these calls, e.g. "spec check"> <calls, at least>
  local out usd basis
  out="$($STANGENT cost estimate --step "$1" --calls "$2" 2>/dev/null || true)"
  usd="$(jq -r '.result.estimateUsd // empty' <<<"$out" 2>/dev/null || true)"
  basis="$(jq -r '.result.basis // empty' <<<"$out" 2>/dev/null || true)"
  if [ -n "$usd" ]; then printf 'estimate: at least %s AI calls, about $%s (%s)\n' "$2" "$usd" "$basis" >&2
  else printf 'estimate: at least %s AI calls, cost unknown (%s)\n' "$2" "${basis:-no estimate available}" >&2; fi
  case "$COST_CAP" in ''|none) ;; *)
    if [ -n "$usd" ] && awk -v a="$usd" -v b="$COST_CAP" 'BEGIN { exit !(a > b) }'; then
      guard_stop over_estimate "the estimate, \$$usd, is over --max-cost \$$COST_CAP; nothing was run. Raise the cap or run fewer."
    fi ;;
  esac
}

# Before every AI call. Sets CALL_BUDGET, what this call may spend; empty when there's no cap or the step has its own.
guard_before_call() {   # [the step's own cap, a multi-turn step's]
  local own="${1:-}" d="$STANGENT_COST_DIR" answer
  CALL_BUDGET=""; GUARD_HOLD=""
  [ ! -f "$d/stopped" ] || return 1
  if [ -z "$COST_CAP" ]; then
    guard_stop no_cap "this step costs money and no cap was chosen. Pass --max-cost <dollars> (or STANGENT_MAX_COST), or --max-cost none to run without one."
    return 1
  fi
  if [ -n "$STOP_AT_USAGE" ] && [ -f "$d/usage-seen" ]; then
    answer="$(jq -r --argjson stop "$STOP_AT_USAGE" 'select((.utilization // 0) * 100 >= $stop) | "\(.rateLimitType) window is \((.utilization * 100) | floor)% used"' "$d/usage-seen" 2>/dev/null | head -n 1)"
    if [ -n "$answer" ]; then guard_stop usage_limit "--stop-at-usage $STOP_AT_USAGE: the $answer"; return 1; fi
  fi
  [ "$COST_CAP" != none ] || return 0
  guard_lock
  answer="$(cat "$d/spent" | awk -v cap="$COST_CAP" -v own="$own" -v jobs="${GUARD_JOBS:-1}" -v heldsum="$(cat "$d"/held/* 2>/dev/null | awk '{ s += $1 } END { print s + 0 }')" -v heldn="$(ls "$d/held" | wc -l | tr -d ' ')" '
    { spent += $1; calls++ }
    END {
      left = cap - spent - heldsum; slots = jobs - heldn; if (slots < 1) slots = 1
      may = left / slots; need = own != "" ? own : (calls ? spent / calls : 0)
      if (left <= 0 || may < need) printf("stop %.2f %.2f\n", (left < 0 ? 0 : left), need)
      else printf("go %.4f\n", (own != "" ? own : may))
    }')"
  case "$answer" in
    go*)
      GUARD_HOLD="$d/held/$$.$RANDOM"
      printf '%s\n' "${answer#go }" >"$GUARD_HOLD"
      [ -n "$own" ] || CALL_BUDGET="${answer#go }"
      guard_unlock ;;
    *)
      guard_unlock
      set -- $answer
      guard_stop cap_reached "--max-cost \$$COST_CAP: \$$2 is left and the next call could take \$$3"
      return 1 ;;
  esac
}

# After every AI call: its cost goes on the ledger, whatever it streamed about usage is kept and shown, and a call that
# failed on a limit stops the run. Words are only read from a failed call: a page may well be about rate limits.
guard_after_call() {   # <the call's whole output> <its result line> <its stderr>; returns 1 when the run must stop
  local raw="$1" usage="$2" err="$3" d="$STANGENT_COST_DIR" cost info text subtype
  [ -z "${GUARD_HOLD:-}" ] || rm -f "$GUARD_HOLD"
  GUARD_HOLD=""
  cost="$(jq -r '.total_cost_usd // 0' "$usage" 2>/dev/null | tail -n 1)"
  printf '%s\n' "${cost:-0}" >>"$d/spent"
  info="$(jq -Rc 'fromjson? | select(type == "object" and .type == "rate_limit_event") | .rate_limit_info' "$raw" 2>/dev/null | tail -n 1 || true)"
  if [ -n "$info" ]; then
    printf '%s\n' "$info" >"$d/usage-seen"
    if [ "$(jq -r '.status' <<<"$info")" != allowed ]; then
      jq -r '"usage: \(.status), \(.rateLimitType // "?") window\(if .utilization then " \((.utilization * 100) | floor)% used" else "" end)\(if .resetsAt then ", resets \(.resetsAt | todate)" else "" end)"' <<<"$info" >&2 || true
    fi
    if [ "$(jq -r '.status' <<<"$info")" = rejected ]; then
      guard_stop usage_limit "Claude Code refused the call on its $(jq -r '.rateLimitType // "usage"' <<<"$info") limit$(jq -r 'if .resetsAt then ", which resets \(.resetsAt | todate)" else "" end' <<<"$info")"
      return 1
    fi
  fi
  jq -e '.type == "result" and (.is_error | not)' "$usage" >/dev/null 2>&1 && return 0
  subtype="$(jq -r '.subtype // ""' "$usage" 2>/dev/null || true)"
  if [[ "$subtype" == *budget* ]] && [ -n "${CALL_BUDGET:-}" ]; then
    guard_stop cap_reached "--max-cost \$$COST_CAP: a call used the \$$CALL_BUDGET left to it"
    return 1
  fi
  text="$(jq -r '(.result // .error // "") | tostring' "$usage" 2>/dev/null || true) $(head -c 2000 "$err" 2>/dev/null || true) $(grep -v '^{' "$raw" 2>/dev/null | head -c 2000 || true)"
  if grep -Eiq "$GUARD_LIMIT_WORDS" <<<"$text"; then
    guard_stop usage_limit "Claude Code said: $(tr '\n' ' ' <<<"$text" | sed -E 's/  +/ /g; s/^ //' | head -c 300)"
    return 1
  fi
  return 0
}

# A step stangent refused, or that ended failed: the batch goes on and the run exits nonzero at the end (I.10).
guard_mark_refused() { jq -e '.ok == false' <<<"$1" >/dev/null 2>&1 && printf '%s\n' "${2:-}" >>"$GUARD_REFUSED"; return 0; }

guard_finish() {   # <the batch's exit code>; exits with the run's
  local rc="$1" d="$STANGENT_COST_DIR" summary code why
  summary="$(awk -v cap="$COST_CAP" '{ s += $1; n++ } END { if (n) printf "spent: %d AI calls, $%.2f%s\n", n, s, (cap != "" && cap != "none") ? " of $" cap : "" }' "$d/spent")"
  [ -z "$summary" ] || printf '%s\n' "$summary" >&2
  if [ -f "$d/stopped" ]; then
    code="$(cut -f1 "$d/stopped")"; why="$(cut -f2- "$d/stopped")"
    printf 'STOPPED (%s): %s\n' "$code" "$why" >&2
    if [ "$code" = no_cap ]; then exit 2; fi
    printf 'Whatever finished is kept and a step cut short records nothing. To resume, run the same command again:\n  %s\n' "$GUARD_CMD" >&2
    exit 5
  fi
  # Not xargs's own code, which differs by system and can be 1: 1 here means only "refused, and the rest went on".
  [ "$rc" -eq 0 ] || exit 125
  if [ -s "$GUARD_REFUSED" ]; then
    printf 'refused or failed: %s\n' "$(sort -u "$GUARD_REFUSED" | tr '\n' ' ')" >&2
    exit 1
  fi
  exit 0
}
