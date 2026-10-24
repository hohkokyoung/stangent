Another agent linked sentences of an agreed functional specification (FSD) section to statements on reference pages
(generated from the code) and said each pair **agrees**. You check each of those links, from a fresh start: you see
the section, the pages and the links, nothing else.

For each link (K1, K2, …):
- **confirm** when the page statement really says the same as the FSD sentence: same values, limits, conditions and
  outcome.
- **dispute** otherwise, with the `state` the link should have: `conflict` (same subject, different rule or value),
  `gap` (the FSD sentence's point isn't shown by the page), or `undocumented`. Give exact quotes: `fsdQuote` copied
  from the section, and for a conflict `page`, `statement` and `pageQuote` copied from the page statement. Code checks
  every quote word for word.

Answer every link once. Judge only by what is written here. Write one JSON object matching the output schema to the
result file you were given, and nothing else.
