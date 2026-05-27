# Security headers + SSRF

## Baseline security headers

Every HTML response should set:

```
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
Content-Security-Policy: default-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=(), interest-cohort=()
```

API-only responses still benefit from:
```
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

## Header notes

- **HSTS**: opts the browser into HTTPS-only for `max-age` seconds. `preload` requires `max-age >= 31536000` (1 year) and submitting your domain to hstspreload.org. Once preloaded, it's effectively permanent — be sure.
- **CSP**: the strict baseline above breaks anything inline. For SPAs that need inline scripts: use a nonce (`<script nonce="abc123">`) with `script-src 'self' 'nonce-abc123'`. Nonce must be per-response, random.
- **X-Frame-Options: DENY** is superseded by `frame-ancestors 'none'` in CSP. Both is fine for legacy browsers.
- **Permissions-Policy**: explicitly disable features you don't use. `interest-cohort=()` opts out of FLoC.

## What to remove

- `Server: nginx/1.18.0` — version disclosure. Strip or genericize.
- `X-Powered-By: PHP/8.1.0` — same. Strip.
- `X-AspNet-Version`, `X-AspNetMvc-Version`, etc.

## SSRF — Server-Side Request Forgery

An endpoint that fetches a URL the user provided. Naive implementation lets attackers reach:

- Internal services (`http://10.0.0.1/admin`)
- Cloud metadata (`http://169.254.169.254/latest/meta-data/iam/`)
- Localhost (`http://127.0.0.1:6379/` — Redis)
- File scheme (`file:///etc/passwd`)

### Defense

1. **Scheme allowlist**: only `http`, `https`.
2. **Resolve hostname and check IP against deny ranges:**

```python
import ipaddress, socket

BLOCKED = [
    ipaddress.ip_network("10.0.0.0/8"),
    ipaddress.ip_network("172.16.0.0/12"),
    ipaddress.ip_network("192.168.0.0/16"),
    ipaddress.ip_network("127.0.0.0/8"),
    ipaddress.ip_network("169.254.0.0/16"),
    ipaddress.ip_network("0.0.0.0/8"),
    ipaddress.ip_network("::1/128"),
    ipaddress.ip_network("fc00::/7"),
    ipaddress.ip_network("fe80::/10"),
]

def is_safe_url(url: str) -> bool:
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https"):
        return False
    if not parsed.hostname:
        return False
    try:
        ips = [info[4][0] for info in socket.getaddrinfo(parsed.hostname, None)]
    except socket.gaierror:
        return False
    for ip in ips:
        ip_obj = ipaddress.ip_address(ip)
        if any(ip_obj in net for net in BLOCKED):
            return False
    return True
```

3. **DNS-pin**: after the check, fetch using the resolved IP, not the hostname. Otherwise an attacker can use a DNS record that resolves to a safe IP at check time and a private IP at fetch time (TOCTOU).

4. **Disable HTTP redirects** or re-check the URL on each redirect.

5. **Outbound proxy with allowlist** is the most robust: all egress goes through a proxy that allows only known destinations.

### Use a library if possible
- Python: `httpx` + manual checks above
- Node: `ssrf-req-filter`
- Go: `safehttp`

## Don't

- `requests.get(user_url)` with no validation.
- `requests.get(user_url, allow_redirects=True)` after only validating the original URL.
- Allow `file://`, `gopher://`, `dict://` schemes.
- Trust hostname-based checks without DNS resolution — `127.0.0.1` has many spellings (`0`, `0177.0.0.1`, `2130706433`, `[::1]`).
