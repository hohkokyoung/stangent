You get one **ticket** (its summary, description and comments) and the **reference pages** that describe the code:
screens, shared components and backend entry points. Code found nothing in the ticket that names a page or a symbol
directly (no stack trace, URL, quoted screen name or class name).

The pages come twice. First, the ones code ranked highest for this ticket's words, in detail: the phrase each shares
with the ticket and some of what it holds. Then every page, one numbered line each. The ranking only matches words: it
is a hint, not an answer. The right page can sit lower, or only in the full list, when the ticket and the code use
different words (the ticket says "NFO", the code "metadata"). Read the full list before you decide.

List the pages (at most 5) where the work for this ticket most likely starts: the screen the reporter is looking at,
the endpoint or handler behind the behaviour they describe. Give each page by its **number** from the list (e.g.
`"page": "37"`). For each page, quote the words from the ticket that point to it, **exactly as they are written in the
ticket** (copy them; at least 4 words, or a whole line). Code checks every number and every quote, and rejects the page
if the quote isn't there.

Use only what the list shows, never your own knowledge of the product. If nothing plausibly matches, return no pages:
that is a valid answer. The bundle then says there's no confident starting point, and the reporter is asked where it
happens.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
