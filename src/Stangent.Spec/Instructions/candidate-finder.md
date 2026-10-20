You get **one section** of a functional specification (FSD) the client agreed, and the **list of reference pages**
that describe the code: screens, shared components and backend entry points, by id and title only. Code found no page
whose name matches this section.

List the pages (at most 5) most likely to describe what this section talks about: the screen it describes, the
endpoint or handler behind the behaviour it describes. Use only the titles and ids given. If nothing in the list
plausibly matches (the section may describe something the code doesn't have), return no candidates: that is a valid
answer, and code then records the section as a gap.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
