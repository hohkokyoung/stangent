# D322 first step, the one fix attempt (frozen before the run, 2026-11-15)

Run 2 (checklist + checks) found 26 of 30, as D321 alone; its misses went where an AI claim was unchecked: three listed clicks
marked "covered" with only a section name. Fix, general (D18: no AI claim unchecked): a "covered" answer must quote the FSD
sentence that covers it, and `check_output.py` checks the quote is word for word inside the named section (its subsections
included). Limit: code can't tell whether the sentence means the click; the claim is visible, not proven.
Everything else as run 2: same checklist (147 items), same prompt with the quote added, Sonnet 5.5, fresh agent.
Scored the same way; the Opus checker on the unplanted sample runs only if found is above 26 (otherwise it can't change the reading).
Reading, frozen (as run 2): the checklist stays if found is above 26, false alarms at most 15.6%, tokens within 10% of
D321's 105K; more found for more tokens goes to the owner as a question on D320's token rule. If found is not above 26,
the checklist is dropped (this is its one fix attempt). Caveat: the cause was found by reading run 2's answers on the key's
items, so this rerun is partly tuned to the key.

Prompt as sent (in full in the session transcript): run 2's, plus for "covered": "Give the section id AND quote, word for word, the FSD sentence in that section that describes this screen or this action. If no sentence describes this action itself (a sentence about the same screen but a different action does not count), it is not covered." That last sentence is also drawn from run 2's misses.
