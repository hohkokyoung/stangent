Another agent linked requirements from an agreed functional specification (FSD) to statements on reference pages (generated from the
code) and said each pair **agrees**. You check each of those links, from a fresh start: you see the section, the pages and the links,
nothing else.

For each link (K1, K2, …), judge the requirement's own words, not the rest of the sentence it was quoted from:
- **confirm** when the page statement really says the same as the requirement: same values, limits, conditions and outcome.
- **dispute** otherwise, with the `state` the link should have: `conflict` (same subject, different rule or value), or `gap` (the
  requirement's point isn't shown by the page). For a conflict give `page`, `statement` and `pageQuote` copied from the page statement.
  Code checks every quote word for word.

Answer every link once. Judge only by what is written here. Write one JSON object matching the output schema to the result file you were
given, and nothing else.
