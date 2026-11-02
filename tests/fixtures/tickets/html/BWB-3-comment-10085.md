_thesnallygaster wrote:_

Exactly the same issue here on 2025.1.3 :|
~~In case it helps anyone I got these errors in the logs around the time I updated from 2024.12.1 to 2025.1.2
I think it may be related to this PR #5088 and may be fixed by this PR #5283~~
You can scratch that, the error I got appears to have gotten there before a database migration that added the missing/required table, that same query executes fine now but the error in Billing > Subscription is still the same
