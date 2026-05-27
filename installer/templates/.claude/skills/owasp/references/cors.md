# CORS — Cross-Origin Resource Sharing

CORS relaxes the browser's same-origin policy. Misconfigured, it hands attackers your authenticated API.

## The dangerous combination

```
Access-Control-Allow-Origin: *
Access-Control-Allow-Credentials: true
```

**Forbidden by spec** — most browsers reject. But many frameworks accept misconfiguration silently in dev mode. Don't ship it.

## The equally-dangerous-but-subtler combination

```python
@app.middleware
async def cors(request, call_next):
    resp = await call_next(request)
    resp.headers["Access-Control-Allow-Origin"] = request.headers.get("origin", "")
    resp.headers["Access-Control-Allow-Credentials"] = "true"
    return resp
```

Echoing whatever `Origin` the client sent is functionally identical to `*` — every site becomes allowed. **Use an allowlist:**

```python
ALLOWED = {"https://app.example.com", "https://admin.example.com"}

origin = request.headers.get("origin")
if origin in ALLOWED:
    resp.headers["Access-Control-Allow-Origin"] = origin
    resp.headers["Vary"] = "Origin"  # tell caches the response varies
    resp.headers["Access-Control-Allow-Credentials"] = "true"
```

## FastAPI CORSMiddleware — safe config

```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["https://app.example.com"],     # never ["*"] with credentials
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE"],
    allow_headers=["Authorization", "Content-Type"],
    max_age=600,
)
```

`allow_origin_regex` is fine if anchored:
```python
allow_origin_regex=r"^https://(app|admin)\.example\.com$"   # anchored
# NOT: r"https://app.example.com"                            # matches subdomain.evil.com/anything?app.example.com
```

## Preflight

Browsers send `OPTIONS` before non-simple requests. Your CORS middleware must respond with the right headers OR your endpoint must allow `OPTIONS`. Easy to forget; common cause of "works in Postman, fails in browser" bugs.

## Default for API-only services

If you don't need cross-origin, **don't set CORS headers at all.** Same-origin works without them. Adding CORS only widens the attack surface.

## Don't

- Use `*` with credentials. Browser will reject; you'll spend hours debugging.
- Reflect Origin without an allowlist.
- Use `allow_origins=["null"]` thinking it's safe — sandboxed iframes get `Origin: null` and an attacker can craft one.
- Skip the `Vary: Origin` header when the response depends on origin — CDNs will serve the wrong response.
- Allow `Access-Control-Allow-Headers: *` — be explicit about what's allowed.
