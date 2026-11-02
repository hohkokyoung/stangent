#!/usr/bin/env python3
"""Turns bitwarden's frozen held-out set (D100, heldout.json) into the ticket layout run.sh and the Jira copy read,
as Sonarr's is: selection.json, answer-key.json and issues/<n>/issue.json.

The ticket text is heldout.json's, unchanged (title, body, the comments posted before the fix merged). GitHub is asked
(read-only) only for what heldout.json left out: who wrote the issue and each comment, and when. A frozen comment
with no exact match on GitHub stops the script. Images stay links in the text; none are downloaded (the file finder
reads text only, D101).

The run commit is the fix's merge commit's first parent; the answer key's files are what the merge commit changed at
their own paths (`git diff --name-only parent merge`), as Sonarr's key and the agent baseline use.

    python3 -I benchmark/tickets/bitwarden/freeze.py <bitwarden clone> benchmark/tickets/bitwarden
"""
import json
import os
import subprocess
import sys

REPO = "bitwarden/server"
PINNED = "9129efbd1531d5d8472345638efad7ef25a7337a"  # select.py's

ISSUE = """query($n:Int!){repository(owner:"bitwarden",name:"server"){issue(number:$n){number createdAt author{login}
comments(first:100){nodes{databaseId body createdAt author{login}}}}}}"""


def gh(query, **variables):
    args = ["gh", "api", "graphql", "-f", "query=" + query]
    for k, v in variables.items():
        args += ["-F" if isinstance(v, int) else "-f", f"{k}={v}"]
    return json.loads(subprocess.run(args, capture_output=True, text=True, check=True).stdout)["data"]


def git(clone, *args):
    return subprocess.run(["git", "-C", clone, *args], capture_output=True, text=True, check=True).stdout


def login(node):
    return (node.get("author") or {}).get("login") or "ghost"


def main(clone, out):
    cases = json.load(open(os.path.join(out, "heldout.json")))
    selection, key = [], {}
    for c in cases:
        n = int(c["id"].lstrip("#"))
        merge = c["fix"]["mergeCommit"]
        parent = git(clone, "rev-parse", merge + "^1").strip()
        files = sorted(git(clone, "diff", "--name-only", parent, merge).splitlines())
        issue = gh(ISSUE, n=n)["repository"]["issue"]
        remote = issue["comments"]["nodes"]
        comments = []
        for body in c["comments"]:
            match = [r for r in remote if r["body"] == body and r["createdAt"] < c["fix"]["mergedAt"]]
            if not match:
                sys.exit(f"#{n}: a frozen comment has no exact match on GitHub")
            r = match[0]
            remote.remove(r)
            comments.append({"id": r["databaseId"], "author": login(r), "createdAt": r["createdAt"], "body": body})
        os.makedirs(os.path.join(out, "issues", str(n)), exist_ok=True)
        with open(os.path.join(out, "issues", str(n), "issue.json"), "w") as f:
            json.dump({"number": n, "title": c["summary"], "body": c["description"], "author": login(issue),
                       "createdAt": issue["createdAt"], "comments": comments, "attachments": []}, f, indent=2, ensure_ascii=False)
            f.write("\n")
        selection.append({"issue": n, "kind": "bug", "area": "backend", "runCommit": parent, "title": c["summary"],
                          "createdAt": issue["createdAt"], "frozenBefore": c["fix"]["mergedAt"]})
        key[str(n)] = {"fixCommit": merge, "parent": parent, "pr": c["fix"]["pr"], "files": files}
        print(f"#{n}: {len(comments)} comment(s), {len(files)} fix file(s), run at {parent[:9]}", file=sys.stderr)
    with open(os.path.join(out, "selection.json"), "w") as f:
        json.dump({"schemaVersion": 1, "repo": REPO, "pinned": PINNED,
                   "rule": "D100; benchmark/tickets/bitwarden/select.py --heldout, laid out by freeze.py",
                   "maxFixFiles": 15, "issues": selection}, f, indent=2, ensure_ascii=False)
        f.write("\n")
    with open(os.path.join(out, "answer-key.json"), "w") as f:
        json.dump({"note": "Never read by the run: the scoring only (D77).", "issues": key}, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
