You get one **ticket** (its summary, description, comments and any text attachments) and read-only access to the
**repo** it is about: Grep, Glob and Read, in the repo only.

Find the files the work for this ticket most likely changes: for a bug, where it goes wrong; for a change request,
where the new behaviour belongs. Search the code: the ticket's own words are your best leads (error messages, log
lines, labels, names, URLs, stack-trace frames). Open the files you name. Name at most 10, most likely first; fewer is
better when you are sure.

For each file give:
- `path`: relative to the repo root, forward slashes, as Glob or Grep showed it.
- `quote`: words from the ticket this file explains, **exactly as they are written in the ticket** (copy them; at
  least 4 words, or a whole line).
- `line`: code from the file, **copied exactly** (at least 12 characters and two words; one line, or a few lines
  joined by spaces), that connects it to the ticket: the message, the condition, the call.
- `why`: one sentence on how the file is involved.

Code checks every path, every quote and every line, and rejects a file whose quote isn't in the ticket or whose line
isn't in the file.

**Outside the code.** Some tickets are about something the code doesn't hold: the server (a full disk, memory),
the network (a proxy, a firewall, DNS), the deployment (config, versions, TLS on the server), live data, or a third
party being down. List each such part under `outside` with its area and the ticket's words, quoted the same way. A
screen, an API, an error the app shows, or a security hole in the app is **in** the code: give its files. A ticket can
have both. Mark a part outside only when the ticket says so; when unsure, leave it out.

Use what you find in the repo, not your own memory of the product. If nothing in the repo plausibly matches, return
no files: that is a valid answer, and the questions draft then asks the reporter where it happens. Don't change any
file. Return one JSON object matching the output schema as your structured output, and nothing else.
