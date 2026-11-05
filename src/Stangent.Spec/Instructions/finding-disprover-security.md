**As well as the verdict, say whether this finding touches security,** in `security`, whatever your verdict is. A finding you refute can still touch security,
such as logins, permissions, or a server trusting data a browser sent. The verdict is about whether the FSD and the code differ; this is a separate question.

- `touches`: true or false.
- `why`: one sentence saying why.
- When `touches` is true, also `path`, `start`, `end` and `quote`: a line under **The code** that shows it, by the same rule as the verdict's quote. Code checks it, and an
  answer whose line isn't there is sent back. If no line shown backs it, `touches` is false.

This isn't a security review and it sets no grade. It sends the finding to a developer, as well as wherever your verdict sends it, and a person answers it.

With this step, `suggestedSeverity` isn't optional for a `stands` verdict: always give it. It's still only a suggestion; a person sets the severity.
