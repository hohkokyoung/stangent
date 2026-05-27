# CSRF — Cross-Site Request Forgery

The attacker tricks a logged-in user's browser into sending a state-changing request to your site. The browser automatically attaches cookies. Your server can't tell the difference between a legitimate click and one originating from `evil.com`.

## When CSRF applies

Only browser-context requests using ambient credentials (cookies, basic auth, client certs). **API-only services using `Authorization: Bearer <jwt>` are immune** — the browser doesn't auto-attach Bearer headers across origins (subject to correct CORS, see cors.md).

## Defenses (use BOTH where possible)

### 1. SameSite cookies

```
Set-Cookie: session=...; HttpOnly; Secure; SameSite=Lax
```

- `SameSite=Strict` — cookie never sent on cross-origin requests. Best protection. Side effect: even legitimate cross-site links to your site don't carry the session — user appears logged out until they hit the site directly.
- `SameSite=Lax` — sent on top-level navigation (clicking a link to your site) but not on cross-origin POSTs. Good default.
- `SameSite=None; Secure` — sent on all cross-origin requests. Only for genuinely cross-site flows (e.g. embedded iframes, federated auth). Requires `Secure`.

### 2. Synchronizer token pattern

Server issues a random token tied to the session. Every state-changing form/request must echo the token. Server rejects on mismatch.

**FastAPI sketch:**
```python
@router.get("/form")
async def form(session: Session = Depends(get_session)):
    token = secrets.token_urlsafe(32)
    session.csrf_token = token
    return render("form.html", csrf=token)

@router.post("/action")
async def action(
    csrf_token: str = Form(...),
    session: Session = Depends(get_session),
):
    if not hmac.compare_digest(csrf_token, session.csrf_token):
        raise HTTPException(403, "csrf check failed")
    ...
```

Use `hmac.compare_digest` to prevent timing oracles.

### 3. Origin / Referer check (defense in depth)

```python
origin = request.headers.get("origin") or request.headers.get("referer", "")
if not origin.startswith("https://app.example.com"):
    raise HTTPException(403)
```

Cheap and surprisingly effective. Not sufficient alone (some browsers strip Referer), but a good extra layer.

## State-changing = which methods?

`POST`, `PUT`, `PATCH`, `DELETE` — anything that mutates server state. `GET` should be safe (idempotent, side-effect-free). If a `GET` mutates, it's a bug regardless of CSRF.

## Don't

- Trust `X-Requested-With: XMLHttpRequest` as your only defense — modern browsers may not block it in all scenarios.
- Use a CSRF token that's the same for every user (session-tied or per-form).
- Put the token in a URL (logs / referers leak it). Form body or custom header.
- Skip CSRF protection on "internal" admin endpoints — those are higher value, not lower.
