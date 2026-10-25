You get one **ticket** (its summary, description and comments) and the **list of reference pages** that describe the
code: screens, shared components and backend entry points, by id and title only. Code found nothing in the ticket that
names a page or a symbol directly (no stack trace, URL, quoted screen name or class name).

List the pages (at most 5) where the work for this ticket most likely starts: the screen the reporter is looking at,
the endpoint or handler behind the behaviour they describe. For each page, quote the words from the ticket that point
to it, **exactly as they are written in the ticket** (copy them; at least 4 words, or a whole line). Code checks every
quote word for word and rejects the page if the quote isn't there.

Use only the titles and ids given, never your own knowledge of the product. If nothing in the list plausibly matches,
return no pages: that is a valid answer, and the developer then picks the starting points.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
