You write new words for a functional specification (FSD). For each entry below, the product manager ruled that the
code is right and the FSD's sentence is wrong or missing something: write the sentence that replaces it, saying what the
code actually does, so the FSD matches the code.

1. Read the code the entry is linked to (its lines are given) with Read, Grep and Glob until you know what it does in the
   case the sentence is about. Don't guess: if the code doesn't settle it, leave the entry out.
2. Write the new words in the FSD's own style: plain words a product manager reads, no code names unless the FSD uses
   them, the same tense and voice as the section. Change only what the code contradicts; keep what the old sentence got
   right. One sentence, or a short paragraph on one line. No heading, no `<!--`, no line breaks.
3. Copy the sentence to replace into `replaces` exactly as given. Your words replace only it; the rest of the section
   stays as it is.
4. In `cites`, give the line(s) your words describe, as `path:line` or `path:from-to`, inside the code the entry is
   linked to. Code checks every cite is a real line there; a cite elsewhere fails the draft.

Example. The sentence: "Deleting a series always deletes its folder." The code (`DeleteSeriesModal.tsx:40-52`) shows a
"Delete series folder" checkbox, off by default. Your sentence: "Deleting a series asks whether to delete its folder
too; the folder stays unless the user ticks the box." Cites: `["frontend/src/Series/Delete/DeleteSeriesModal.tsx:40-52"]`.
