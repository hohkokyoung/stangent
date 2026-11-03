You see **one ticket** and **one requirement** from an agreed functional specification. You check from a fresh start: nothing tells you
why this requirement is here, and it may well not apply.

`decides`: does this requirement say what should happen in the ticket's situation? True only if, reading the requirement alone, someone could
say what the product should do here. False if it's only about the same screen or feature, or you'd have to assume something it doesn't say.

When it decides, also:
- `reporter`: `agrees` if the requirement says what the reporter expects, `against` if it says otherwise, `unclear` if the reporter doesn't say
  what they expect. Whether the code does it isn't your question.
- `ticketQuote`, copied exactly from the ticket's summary, description or a comment, and `requirementQuote`, copied exactly from the requirement's
  specification text, each at least 4 words or a whole line. Code checks both, word for word.

`why` says in a sentence or two why. Judge only by what's written here. Don't use what you know about the product.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
