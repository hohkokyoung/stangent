#!/usr/bin/env python3
"""D339 with subagents (headless Claude Code hit its weekly limit, 2026-11-15): lay out one export per ticket and side.

    python3 -I benchmark/tickets/d339_prepare.py <clone> <tickets dir> <graphs dir> <stangent.dll> <out dir>

For each of the multi-file bugs (the key's fix changes 2+ files): `<out>/<KEY>.ticket.md` (the ticket's text, as
agent_baseline.py builds it), `<out>/<KEY>-A/` (the code at the fix's parent by `git archive`, nothing else) and
`<out>/<KEY>-B/` (the same, plus `.stangent/graph.sqlite` linked to the graph built at that parent, a 3-line
`stangent.yaml`, and `.bin/stangent`, a wrapper that runs stangent in that folder). Prints the keys, one per line.
No AI. An existing folder is left as it is.
"""
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from agent_baseline import ticket_text  # noqa: E402


def main(clone, tickets, graphs, dll, out):
    key = json.load(open(os.path.join(tickets, "answer-key.json")))["issues"]
    jira = json.load(open(os.path.join(tickets, "jira-keys.json")))
    os.makedirs(out, exist_ok=True)
    for n, v in key.items():
        if len(v["files"]) < 2:
            continue
        k = jira[n]
        t = json.load(open(os.path.join(clone, ".stangent", "tickets", k, "ticket.json")))
        with open(os.path.join(out, f"{k}.ticket.md"), "w") as f:
            f.write(ticket_text(t) + "\n")
        for side in ("A", "B"):
            d = os.path.join(out, f"{k}-{side}")
            if os.path.exists(d):
                continue
            os.makedirs(d)
            archive = subprocess.run(["git", "-C", clone, "archive", v["parent"]], capture_output=True, check=True).stdout
            subprocess.run(["tar", "-x", "-C", d], input=archive, check=True)
            if side == "B":
                os.makedirs(os.path.join(d, ".stangent"))
                os.symlink(os.path.join(graphs, f"{n}.sqlite"), os.path.join(d, ".stangent", "graph.sqlite"))
                with open(os.path.join(d, "stangent.yaml"), "w") as f:
                    f.write("version: 1\nstore:\n  root: .stangent/store\n")
                os.makedirs(os.path.join(d, ".bin"))
                w = os.path.join(d, ".bin", "stangent")
                with open(w, "w") as f:
                    f.write(f'#!/bin/sh\ncd "{d}" && exec dotnet "{dll}" "$@"\n')
                os.chmod(w, 0o755)
        print(k)


if __name__ == "__main__":
    main(*sys.argv[1:6])
