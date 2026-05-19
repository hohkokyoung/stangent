# ADR Contradiction Check

For each Accepted ADR in `decisions.md`, read its **Consequences** section.
Determine: does the raw_request or its most natural implementation approach
violate any consequence rule?

## Examples of contradictions to detect

- ADR says "all DB access via repository classes" → request implies querying the DB directly inside a service or route handler
- ADR says "use BLoC for all state management" → request mentions using Provider or setState
- ADR says "all screens must use ConsumerWidget" → request implies StatefulWidget
- ADR says "HTTP calls via Dio" → request references the `requests` library
- ADR says "use JWT auth" → request implies session cookies

## For each contradiction found, record

```
{
  adr_id:    "ADR-NNN",
  adr_title: "...",
  rule:      "exact consequence text from ADR",
  conflict:  "what the request implies that violates it",
  options: [
    "A — Adjust feature approach to comply with {adr_title}",
    "B — Override {adr_id} for this feature (reason required)",
    "C — Cancel this feature"
  ]
}
```

Store all findings as `contradiction_list`. An empty list means no conflicts.
