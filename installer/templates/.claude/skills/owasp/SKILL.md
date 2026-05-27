# SKILL: owasp

## Purpose
Governs security review and secure-by-default implementation for code with web surface: HTTP endpoints, browser-facing UIs, forms, file uploads, user input, DB queries built from input, cookies, auth flows, cross-origin calls. Scope is OWASP Top 10 (2021) plus the still-critical OWASP 2017 entries (XSS, CSRF). Does NOT cover infrastructure hardening, container/k8s security, or platform-level concerns.

## Rules

1. **Never build SQL/NoSQL/command strings from user input.** Use parameterized queries, prepared statements, or ORM bindings. String concatenation or f-strings around user values is an automatic `[OWASP-A03 Injection]` flag.
2. **Encode at the sink, not at the source.** HTML output → HTML-encode. URL → URL-encode. JS string → JS-encode. Encoding is per-context; storing already-encoded values is wrong.
3. **Cookies that carry session: `HttpOnly; Secure; SameSite=Lax` (or `Strict`).** Auth tokens never in non-HttpOnly cookies, never in localStorage if you can avoid it.
4. **CSRF protection on every state-changing request from a browser context.** Either `SameSite=Strict/Lax` cookies + origin check, OR a CSRF token. API-only services using `Authorization: Bearer` are immune by virtue of CORS — but only if CORS is correctly configured (rule 5).
5. **CORS: explicit origin allowlist; never `Access-Control-Allow-Origin: *` with `Access-Control-Allow-Credentials: true`.** The combination is forbidden by spec and dangerous. Default: no CORS headers (same-origin only).
6. **Access control checks happen on the SERVER, on every request, by the row's owner field — not by what the client asks.** `WHERE id = $1 AND user_id = auth.uid()`, never `WHERE id = $1` trusting that the client only sent its own ids.
7. **No secrets in code, logs, error messages, or URLs.** `.env` + vault. Audit logger.info / Sentry breadcrumbs / stack traces for token/key/PII leakage.
8. **Validate uploads: extension allowlist + MIME-by-content + size cap.** Store outside web root; serve via signed URLs or a separate domain.
9. **Set baseline security headers**: `Strict-Transport-Security`, `Content-Security-Policy` (at least `default-src 'self'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` for unused features.
10. **Outbound HTTP from user-supplied URLs (SSRF risk)**: block private IP ranges (10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, fc00::/7), block link-local, and DNS-pin if possible.

## Patterns

- **Parameterized SQL (Python + asyncpg / SQLAlchemy):**
  ```python
  await db.execute("SELECT * FROM users WHERE email = $1", email)
  # NEVER: f"SELECT * FROM users WHERE email = '{email}'"
  ```
- **HTML escaping in templates** — use Jinja2 autoescape, React JSX (auto), or `html.escape()` for raw strings.
- **CORS allowlist (FastAPI):**
  ```python
  app.add_middleware(
      CORSMiddleware,
      allow_origins=["https://app.example.com"],   # never ["*"] with credentials
      allow_credentials=True,
      allow_methods=["GET", "POST"],
      allow_headers=["Authorization", "Content-Type"],
  )
  ```
- **CSRF token pattern (form-based):** double-submit cookie OR session-tied synchronizer token; reject if missing or mismatched on POST/PUT/DELETE/PATCH.
- **Authorization at row level (Supabase pattern):** `USING (auth.uid() = user_id)` on every SELECT/UPDATE policy.
- **Secret loading:**
  ```python
  SECRET = os.environ["STRIPE_KEY"]  # at startup only; let it KeyError if missing
  ```

## Anti-patterns

- `cur.execute(f"... WHERE name = '{name}'")` — SQL injection. **`[OWASP-A03]` blocking.**
- `innerHTML = userInput` in JS, or `dangerouslySetInnerHTML` without sanitization. **`[OWASP-A03 XSS]` blocking.**
- `document.cookie = "session=...; path=/"` without `HttpOnly` (not possible from JS — that's the point; if you see this pattern, sessions aren't on `HttpOnly`).
- `Access-Control-Allow-Origin: *` with `Access-Control-Allow-Credentials: true`. **`[OWASP-A05 Misconfig]` blocking.**
- `Access-Control-Allow-Origin: <echo of request Origin>` without an allowlist check. Same as `*` in effect.
- Auth check by `if request.user_id == requested_user_id` *trusting the client to send only its own id*. **`[OWASP-A01 BAC]` blocking.**
- `logger.info(f"login failed for {email} with password {pw}")` — secrets in logs. **`[OWASP-A09]` blocking.**
- Returning detailed error messages to clients: stack traces, SQL fragments, internal paths. **`[OWASP-A05]` concerns.**
- Storing passwords as plaintext, MD5, SHA1, or unsalted SHA-256. Use Argon2id or bcrypt (cost ≥ 12). **`[OWASP-A02]` blocking.**
- `requests.get(user_supplied_url)` with no scheme/host validation. **`[OWASP-A10 SSRF]` blocking.**
- File upload: trusting `Content-Type` header or filename extension alone. **`[OWASP-A04]` concerns.**
- JWT with `alg: none` accepted, or shared secret short enough to brute. **`[OWASP-A02]` blocking.**
- Wide `Permissions-Policy` (e.g. `camera=*, microphone=*`) without need.

## Severity tagging convention

When the reviewer / tester flags a finding, tag it with the OWASP category:

- `[OWASP-A01]` Broken Access Control
- `[OWASP-A02]` Cryptographic Failures
- `[OWASP-A03]` Injection (incl. SQL, NoSQL, command, XSS)
- `[OWASP-A04]` Insecure Design
- `[OWASP-A05]` Security Misconfiguration (incl. CORS, headers)
- `[OWASP-A06]` Vulnerable & Outdated Components
- `[OWASP-A07]` Identification & Authentication Failures (incl. CSRF in browser flows)
- `[OWASP-A08]` Software & Data Integrity Failures
- `[OWASP-A09]` Security Logging & Monitoring Failures
- `[OWASP-A10]` Server-Side Request Forgery (SSRF)
