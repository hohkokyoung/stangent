# Secrets handling + safe logging

## Secrets — where they live

| Location | OK? | Notes |
|---|---|---|
| `.env` files (gitignored) | ✅ dev | Production: use a real secret manager |
| Process env (`os.environ`) | ✅ | Load at startup, fail-fast if missing |
| Vault / AWS Secrets Manager / GCP Secret Manager | ✅ prod | The right answer at scale |
| Source code | ❌ | Even "just for now" — leaks via git history |
| Public docker images | ❌ | Bake-time secrets visible in layer |
| Frontend bundles | ❌ | Browser users see everything |
| Logs / Sentry / DataDog | ❌ | See "what never appears in logs" |
| URLs / query strings | ❌ | Server logs, proxy logs, referer headers |
| LocalStorage | ❌ | Any XSS reads everything |

## Loading pattern (fail-fast)

```python
import os
from functools import lru_cache

@lru_cache
def secrets():
    return {
        "stripe_key":  os.environ["STRIPE_KEY"],       # KeyError at boot if missing
        "jwt_secret":  os.environ["JWT_SECRET"],
        "db_url":      os.environ["DATABASE_URL"],
    }

# at import time:
_ = secrets()  # eager validation; never crash mid-request because env is unset
```

`os.environ.get("KEY", "")` is wrong — silent empty secret. Use `os.environ["KEY"]` and let it crash.

## Rotation

- Every secret has an owner and a rotation cadence.
- Rotation must be possible without redeploy (config service or hot-reload).
- After a leak, rotation must complete in < 1 hour.

## What never appears in logs

- Passwords, tokens, API keys, session IDs, OAuth codes
- Full credit card numbers / CVV (PCI)
- SSN, government IDs, medical data (PHI / HIPAA)
- Email addresses + IP addresses *correlated* (GDPR)
- Anything inside `Authorization`, `Cookie`, `Set-Cookie` headers
- Request bodies on auth endpoints (`/login`, `/reset-password`, `/oauth/callback`)

## Safe logging pattern

```python
SAFE_HEADERS = {"user-agent", "host", "x-request-id"}

def safe_headers(headers):
    return {k: v for k, v in headers.items() if k.lower() in SAFE_HEADERS}

logger.info("request", extra={"path": path, "headers": safe_headers(req.headers)})
# NOT: logger.info(f"request: {req}")  -- dumps everything
```

For PII you must log: hash it. `sha256(email + pepper)` is the same across runs (correlatable) but reveals nothing on its own.

## Error responses — don't leak internals

```python
@app.exception_handler(Exception)
async def handler(req, exc):
    logger.exception("server error", extra={"path": req.url.path})  # full trace in logs
    return JSONResponse(status_code=500, content={"detail": "internal error"})
                                                                # generic to client
```

Never return stack traces, SQL snippets, file paths, library versions, or framework internals to clients. Attackers reconstruct your stack from these.

## Don't

- `print(secret)` for "debugging just for a sec".
- `repr(user)` in logs if `User` has a `password_hash` attribute.
- Returning the failed auth header back in a 401 body.
- Allowing `?debug=1` to flip stack traces back on in production.
- Pickling sessions to disk in a world-readable temp file.
