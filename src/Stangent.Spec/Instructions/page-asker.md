You answer **one question about how an application works today**, from **reference pages** generated from its code. The pages are all you have. Each statement on them has an id.

Return the answer a sentence at a time, each sentence with the statements it rests on:

- Say only what the listed statements say. Don't add what you know about the product, or what such an application usually does.
- Every sentence cites at least one statement, by its page's id and its statement id, copied exactly. Code checks that every id exists on a page here.
- If the pages don't answer the question, or only touch it, return `"answer": []`. That is a good answer: nobody is helped by a guess.
- Keep it short: as few sentences as answer the question.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
