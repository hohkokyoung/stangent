#!/usr/bin/env python3
"""bitwarden/server's 5-bug check for the page index (D87).

The 5 newest (by when they were filed) closed GitHub bug reports (label "bug", closed as completed) whose merged bitwarden/server PR closes the
issue or says it fixes it, and changes non-test C#. The ticket is the issue's title, body and the comments posted
before the fix merged. The fix's files are traced through renames to the pinned commit; a file that no longer exists
is kept as it was (it simply matches no page). Renames come from one `git diff -M` between the fix and the pinned
commit, so a file that moved and also changed a lot can be missed; a PR with more than 100 files stops the script.

    python3 -I benchmark/tickets/bitwarden/select.py <bitwarden clone> > benchmark/tickets/bitwarden/index-cases.json

Needs `gh` signed in (read-only GitHub API calls) and a full-history clone.
"""
import json
import re
import subprocess
import sys

REPO = "bitwarden/server"
PINNED = "9129efbd1531d5d8472345638efad7ef25a7337a"  # benchmark/repos.tsv
COUNT = 5

ISSUES = """query($c:String){repository(owner:"bitwarden",name:"server"){issues(first:50,after:$c,states:CLOSED,labels:["bug"],
orderBy:{field:CREATED_AT,direction:DESC}){pageInfo{hasNextPage endCursor} nodes{number title createdAt stateReason body
comments(first:50){nodes{body createdAt}}
closedByPullRequestsReferences(first:5,includeClosedPrs:false){nodes{number merged repository{nameWithOwner}}}
timelineItems(itemTypes:[CROSS_REFERENCED_EVENT,CONNECTED_EVENT],first:30){nodes{__typename
... on CrossReferencedEvent{source{... on PullRequest{number merged repository{nameWithOwner}}}}
... on ConnectedEvent{subject{... on PullRequest{number merged repository{nameWithOwner}}}}}}}}}}"""

PR = """query($n:Int!){repository(owner:"bitwarden",name:"server"){pullRequest(number:$n){number title body mergedAt
mergeCommit{oid} changedFiles files(first:100){nodes{path}}}}}"""


def gh(query, **variables):
    args = ["gh", "api", "graphql", "-f", "query=" + query]
    for k, v in variables.items():
        args += ["-F" if isinstance(v, int) else "-f", f"{k}={v}"]
    return json.loads(subprocess.run(args, capture_output=True, text=True, check=True).stdout)["data"]


def server_pr(p):
    return p and p.get("merged") and p["repository"]["nameWithOwner"] == REPO


def fixes(issue):
    """The issue's merged server PRs: those closing it, then those that only mention it."""
    seen = []
    for p in issue["closedByPullRequestsReferences"]["nodes"]:
        if server_pr(p) and p["number"] not in [x for x, _ in seen]:
            seen.append((p["number"], True))
    for t in issue["timelineItems"]["nodes"]:
        p = t.get("source") or t.get("subject")
        if server_pr(p) and p["number"] not in [x for x, _ in seen]:
            seen.append((p["number"], False))
    return seen


def code_files(files):
    """Non-test C#: not under a test/ or *.Test(s)/ folder, not a *Test(s).cs or *Fixture.cs file."""
    test = re.compile(r"(^|/)(tests?|[^/]*\.(Test|Tests|IntegrationTest))/|(Tests?|Fixture)\.cs$", re.I)
    return [f for f in files if f.endswith((".cs", ".cshtml")) and not test.search(f)]


def renames(clone, commit):
    """Paths moved between the fix's merge commit and the pinned commit: old → new."""
    out = subprocess.run(["git", "-C", clone, "-c", "diff.renameLimit=100000", "diff", "-M", "--name-status", commit, PINNED],
                         capture_output=True, text=True, check=True).stdout
    moved = {}
    for line in out.splitlines():
        parts = line.split("\t")
        if parts[0].startswith("R") and len(parts) == 3:
            moved[parts[1]] = parts[2]
    return moved


def main(clone):
    cases, cursor = [], None
    while len(cases) < COUNT:
        page = gh(ISSUES, **({"c": cursor} if cursor else {}))["repository"]["issues"]
        for issue in page["nodes"]:
            if len(cases) == COUNT or issue["stateReason"] != "COMPLETED":
                continue
            for number, closes in fixes(issue):
                pr = gh(PR, n=number)["repository"]["pullRequest"]
                says = re.search(rf"(fix|clos|resolv)\w*:?\s+(https://github.com/{REPO}/issues/|#){issue['number']}\b",
                                 (pr["body"] or "") + " " + pr["title"], re.I)
                files = [f["path"] for f in pr["files"]["nodes"]]
                if pr["changedFiles"] > len(files):
                    sys.exit(f"PR #{pr['number']} changes {pr['changedFiles']} files; only {len(files)} were read")
                if not (closes or says) or not code_files(files):
                    continue
                moved = renames(clone, pr["mergeCommit"]["oid"])
                before = [c["body"] for c in issue["comments"]["nodes"] if c["createdAt"] < pr["mergedAt"]]
                cases.append({
                    "id": f"#{issue['number']}", "summary": issue["title"], "description": issue["body"] or "",
                    "comments": before, "files": sorted({moved.get(f, f) for f in files}),
                    "fix": {"pr": pr["number"], "mergeCommit": pr["mergeCommit"]["oid"], "mergedAt": pr["mergedAt"]},
                })
                break
        if not page["pageInfo"]["hasNextPage"]:
            break
        cursor = page["pageInfo"]["endCursor"]
    json.dump(cases, sys.stdout, indent=1, ensure_ascii=False)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main(sys.argv[1])
