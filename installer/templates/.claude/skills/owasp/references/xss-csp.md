# XSS defense + Content Security Policy

## Three XSS types

| Type | Payload path | Defense |
|---|---|---|
| Reflected | request → response HTML, same flow | HTML-escape output; CSP |
| Stored | request → DB → response HTML, later | HTML-escape on read OR on write (pick one consistently); CSP |
| DOM | request → client JS sinks (`innerHTML`, `eval`) | Use safe DOM APIs; never `innerHTML` with input; CSP `script-src` |

## Output encoding — context matters

| Context | Encoding |
|---|---|
| HTML body | `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, `"` → `&quot;`, `'` → `&#x27;` |
| HTML attribute | quote-attribute + same as above |
| JS string literal | `\xHH` hex-encode anything non-alphanumeric |
| URL parameter | percent-encode |
| CSS value | hex-encode (`\HHHHHH`) |

React JSX auto-escapes HTML body and attributes. **It does NOT escape `dangerouslySetInnerHTML` or `href={javascript:...}`** — handle those yourself.

## Content Security Policy

A defense-in-depth header that blocks unauthorized script execution even if an XSS payload lands.

**Strict baseline:**
```
Content-Security-Policy:
  default-src 'self';
  script-src 'self';
  style-src 'self' 'unsafe-inline';
  img-src 'self' data: https:;
  connect-src 'self' https://api.example.com;
  frame-ancestors 'none';
  base-uri 'none';
  form-action 'self';
```

**Common mistakes:**
- `script-src 'self' 'unsafe-inline'` — defeats the purpose.
- `script-src *` — same.
- Forgetting `frame-ancestors 'none'` (or `X-Frame-Options: DENY`) → clickjacking.

**For a static SPA loading scripts from a CDN:**
```
script-src 'self' https://cdn.example.com 'sha256-<digest of inline init>';
```
Pin by hash or nonce; never `'unsafe-inline'` in production.

## Trusted Types (modern browsers)
```
Content-Security-Policy: require-trusted-types-for 'script'; trusted-types default;
```
Forces all `innerHTML`-style sinks to receive a `TrustedHTML` object built via a policy you define — eliminates an entire class of DOM XSS.

## Don't
- Sanitize on write only and trust forever. New encoding contexts emerge (an admin panel renders content differently).
- Allow user-supplied URLs in `href` without scheme validation (`javascript:alert()` works).
- Concatenate strings into `eval()`, `setTimeout(string, ...)`, or `Function(...)`.
