#!/usr/bin/env python3
"""D375/D406: do what Jira would for a replayed ticket, since replay can't post.

usage: reply.py <clone> <replay-dir> <KEY> <reply-text-file> [--tone internal]

1. Marks the ticket's questions draft (.stangent/tickets/<KEY>/drafts/questions-<tone>.json) as posted, with a
   comment id, as `stangent ticket post` would record it.
2. Adds to the replay ticket (<replay-dir>/<KEY>/raw/getJiraIssue.json) the question as that comment, then the PM's
   reply (the text file's contents) as the next comment, by "Planter PM".
Then: `stangent ticket pull <KEY>` drafts the ruling (uncommitted), a commit as Planter PM, `fsd rulings-check`, and
the second proof. Refuses if the draft holds no PM question or was already posted. Standard library only.
"""
import json, sys, datetime, pathlib

ASKED = "Reply with a line starting `Ruling:`"
QUESTION_ID, REPLY_ID = "30001", "30002"


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    tone = "internal"
    if "--tone" in argv:
        tone = argv[argv.index("--tone") + 1]
        args.remove(tone)
    if len(args) != 4:
        sys.exit(__doc__)
    clone, replay, key, reply_file = pathlib.Path(args[0]), pathlib.Path(args[1]), args[2], pathlib.Path(args[3])
    draft_path = clone / ".stangent" / "tickets" / key / "drafts" / f"questions-{tone}.json"
    draft = json.loads(draft_path.read_text())
    text = draft.get("text") or ""
    if ASKED not in text:
        sys.exit(f"{draft_path} holds no question for the PM; nothing to post")
    if draft.get("posted"):
        sys.exit(f"{draft_path} is already posted: {draft['posted']}")
    now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0)
    draft["posted"] = {"by": "reply.py (replay)", "at": now.strftime("%Y-%m-%dT%H:%M:%SZ"), "commentId": QUESTION_ID}
    draft_path.write_text(json.dumps(draft, ensure_ascii=False, indent=2) + "\n")

    raw_path = replay / key / "raw" / "getJiraIssue.json"
    raw = json.loads(raw_path.read_text())
    block = raw["content"][0]
    payload = json.loads(block["text"])
    fields = payload["data"]["fields"]
    comments = fields.setdefault("comment", {"comments": [], "total": 0, "maxResults": 0, "startAt": 0})
    if any(c.get("id") in (QUESTION_ID, REPLY_ID) for c in comments["comments"]):
        sys.exit(f"{raw_path} already has the question or reply comments")
    def stamp(t):
        return t.strftime("%Y-%m-%dT%H:%M:%S.000+0000")
    comments["comments"].append({"id": QUESTION_ID, "author": {"displayName": "Stangent (posted by the developer)"},
                                 "body": text, "created": stamp(now)})
    comments["comments"].append({"id": REPLY_ID, "author": {"displayName": "Planter PM"},
                                 "body": reply_file.read_text().strip(), "created": stamp(now + datetime.timedelta(minutes=1))})
    comments["total"] = comments["maxResults"] = len(comments["comments"])
    block["text"] = json.dumps(payload, ensure_ascii=False)
    raw_path.write_text(json.dumps(raw, ensure_ascii=False))
    print(json.dumps({"ok": True, "key": key, "draft": str(draft_path), "question": QUESTION_ID, "reply": REPLY_ID}))


if __name__ == "__main__":
    main(sys.argv[1:])
