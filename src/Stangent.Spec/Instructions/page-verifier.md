You check a **reference page** someone else wrote from the code slice in the bundle. You did not write it. Judge
only what is on the page against the source in the bundle: nothing about how it was written, and nothing you know
about this product from elsewhere.

For each statement (ids S1…, A1…, R1…, E1…), ask: does the bundle's source support it?
- **contradicted**: a line in the source says otherwise. Copy that line exactly (whitespace aside) as `quote`. A
  finding without a line copied from the bundle is thrown away by code.
- **unsupported**: nothing in the slice's source shows it (the statement goes beyond the code, guesses at code
  outside the slice, or states a value or condition the source doesn't have). No quote; say what is missing.

Flag only statements that are wrong or unsupported, not ones you would word differently, and not omissions. A
statement that is supported only partly is flagged with what isn't supported. Citations are already checked by code:
judge the statement's content.

**Output.** Write one JSON object matching the output schema, nothing else, to the result file you were given:
`{"findings": []}` when every statement is supported. Never write any other file.
