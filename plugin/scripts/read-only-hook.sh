#!/usr/bin/env bash
# Client code read-only (T.8, D126), a PreToolUse hook for analysis steps. It stops a step writing anywhere but stangent's
# own .stangent/ folder, and running commands. Exit 2 refuses the tool call and shows the reason to the agent.
#
# Enforced when STANGENT_READ_ONLY is set (a driver's analysis step), or when the agent is one of stangent's, from the
# hook's `agent_type`. The bug reproducer is the exception: it builds and runs the app and writes its test (D75), under
# rules of its own. Any other session, and a person's own main session, is left alone.
set -uo pipefail
input="$(cat)"
agent="$(jq -r '.agent_type // ""' <<<"$input" 2>/dev/null || echo "")"
if [ -z "${STANGENT_READ_ONLY:-}" ]; then
  case "$agent" in
    *stangent-bug-reproducer) exit 0 ;;
    stangent-*|stangent:*) ;;
    *) exit 0 ;;
  esac
fi
refuse() { printf 'stangent: this analysis step is read-only: %s\n' "$1" >&2; exit 2; }
tool="$(jq -r '.tool_name // ""' <<<"$input" 2>/dev/null)" || refuse "couldn't read the hook's input"
case "$tool" in
  Bash) refuse "no commands in an analysis step" ;;
  Write|Edit|MultiEdit|NotebookEdit)
    path="$(jq -r '.tool_input.file_path // .tool_input.notebook_path // ""' <<<"$input")"
    cwd="$(jq -r '.cwd // ""' <<<"$input")"; cwd="${cwd:-$PWD}"
    case "$path" in
      '') refuse "$tool with no path" ;;
      *..*) refuse "$path: no .. in a path" ;;
      /*) ;;
      *) path="$cwd/$path" ;;
    esac
    # By the real folder: macOS's temp folder is behind a link, and a link inside .stangent mustn't lead out of it.
    dir="$(dirname "$path")"; rest=""
    while [ ! -d "$dir" ] && [ "$dir" != / ]; do rest="/$(basename "$dir")$rest"; dir="$(dirname "$dir")"; done
    real="$(cd "$dir" && pwd -P)$rest"
    base="$(cd "$cwd" 2>/dev/null && pwd -P)" || refuse "no working folder"
    case "$real/" in
      "$base/.stangent/"*) exit 0 ;;
      *) refuse "$path is outside .stangent/" ;;
    esac ;;
esac
exit 0
