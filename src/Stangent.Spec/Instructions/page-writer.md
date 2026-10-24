You write one **reference page**: a plain description of what this screen, component or backend entry point does,
taken only from the code in this bundle. BAs and PMs read it, and it is later compared sentence by sentence with
the functional specification the client agreed (the FSD). Code checks every citation; a person reads the rest.

**Write only what the source below shows.** The slice is the walk from the page's entry point through the code
graph. Don't use what you know about this product or similar products, and don't guess what code outside the
slice does. If the slice stops before an answer (an endpoint whose code isn't here, a "couldn't see" line), leave it
out: code appends what the slice couldn't see.

**Statements.** One fact per statement, one or two plain sentences, present tense. Name things the way a user sees
them when the code shows it (labels, button text, column headers, messages); otherwise describe them plainly. Don't
describe styling, layout, CSS classes or how the code is organised (hooks, selectors, component names), unless that is
the only name a thing has.

**Citations.** Every statement cites one or more symbol ids from the "Symbols" table, copied exactly: the symbols
whose source shows the fact. Cite nothing outside the table.

**Sections.**
- `shows`: what it displays or returns: fields, columns, lists, states (empty, loading, error), what's hidden when.
- `actions`: what a user (or, for a backend page, a caller or an event) can do or trigger, and what that does. When
  the slice links an action to an endpoint, list the endpoint's id in `endpoints`.
- `rules`: validations, limits, required fields, defaults, allowed values, permissions, ordering, conditions. Be exact:
  give the numbers, values and conditions the code uses ("at most 3", "must be unique", "only when monitored").
- `sideEffects`: what changes elsewhere: records saved or deleted, events or commands sent (and the handlers the
  slice lists for them), files, notifications, messages, calls to other services.

An empty section is fine. Prefer fewer, exact statements over many vague ones; most pages need 5 to 40.

**A previous version** of the page may be given at the end. Then the code changed since it was written: keep every
statement that still holds, word for word, and change, add or remove only what the change affects.

**Output.** Write one JSON object matching the output schema, nothing else, to the result file you were given.
Never write any other file.
