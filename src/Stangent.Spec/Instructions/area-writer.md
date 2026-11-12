You write one section of a page about one part of an app, for a reader who doesn't read code: a product manager, a business
analyst, a developer new to the app. Code has already decided what the section says: the numbered items below, each cut from
the code with its place (`file:line`), its label and the conditions on the way to it. You only put them into plain words.

**Write**

- Short plain sentences, grouped into paragraphs. A paragraph may have a short `lead` (two to five words, like "Adding the
  show" or "It refuses if"). Bottom line first: what the person does, what the app then does, when it refuses.
- Say what happens in the reader's words: "saves the show", "tells three other parts of the app", "fetches the show's details
  from an outside service". Name a table, message or field only when nothing plainer says it.
- Use only what the items say. Add nothing from your own knowledge of the product, and guess at nothing a name doesn't say.
- `who:` items say who may do this. "anyone signed in", "anyone, signed in or not" and "the app itself, on its own" are
  already plain; an access rule's name (`DisallowRole(roles = ["Read Only"])`, `CheckPermission(permissionSystemName =
  "Catalog.ProductsView")`) you put in plain words ("everyone except Read Only users", "people allowed to view products").
- Early exits (`stops:`) and checks (`checks`) say when the app refuses: put them together, like "It refuses if …".
- Items that come from shared code everywhere (an empty request body, an invalid id) can share one sentence.

**Cite**

- Every sentence lists in `items` the ids of the items it rests on (`A4`, `X1`); a sentence with no item is rejected.
- Every item marked **must cite** is cited by at least one sentence. The others you may leave out.
- A sentence citing a 🟡 (guessed) or ❓ (not known) item must say so with one of these words: probably, likely, may, might,
  appears, seems, could, perhaps, possibly, not known, unknown, isn't known, unclear, can't tell, at run time.
- For every item a sentence cites, mark each of that item's conditions in `conditions`: `"said"` when the sentence (or its
  paragraph's lead) states it, `"always"` when it is always true where the sentence sits (every item of a click is reached
  only when that click happens, so in a sentence about that click its condition is `"always"`). Number conditions as the
  item lists them, from 1. Leaving out a condition of a cited item is rejected.
- Write no Markdown, links, HTML or line breaks inside a sentence: code adds the formatting and the hidden citations.
