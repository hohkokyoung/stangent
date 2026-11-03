Another agent turned one section of an agreed functional specification (FSD) into items, each citing exact sentences from
the section. A business analyst will approve these items, and people and agents will then treat them as the truth. You
check them from a fresh start: you see the section and what was made from it, nothing else.

For each item (`r1`, `d1`, `j1`, `s1`, …) ask: **does it say more than its own quotes do?** The rest of the section only
helps you read the quotes; it can't support an item its quotes don't.
- **ok:** everything it says, including its title and acceptance criteria, is stated by its quotes. Saying it in plainer
  words is fine. A criterion's example value ("Firefly", "10 selected") is fine.
- **says-more:** it adds a limit, condition, outcome, actor, or behaviour its quotes don't state, or makes something firmer
  or wider than the quotes do ("must" where the FSD says "can", "everywhere" where it names three places). A journey's or
  scenario's order, kind or steps go too far if its quotes don't state them. An outcome with no quote behind it is always
  says-more, and so is stating as settled what a question for the BA leaves open. Put the item's own words that go too far
  in `extra`, copied exactly, and say why in `note`.
- **padded:** one of its quotes has nothing to do with what it says. Quotes must back the item, and a rule hidden in an
  unrelated item's quotes is read by nobody. Put that quote in `extra`, copied exactly, and say why in `note`.

For each sentence marked background (`b1`, `b2`, …) ask: **is it really background?**
- **ok:** nothing the system must do or allow.
- **needs-item:** it states a rule, field, limit or behaviour, however minor, so it should have an item. Say why in `note`.

Judge only by what's written here. Don't use what you know about the product. Answer everything once, in `checks`.

Write one JSON object matching the output schema to the result file you were given, and nothing else.
