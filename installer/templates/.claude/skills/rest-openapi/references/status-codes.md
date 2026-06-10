# HTTP Status Codes

## Decision tree

| Situation | Code |
|---|---|
| Successful read | 200 |
| Resource created | 201 (include `Location` header) |
| Successful write, no body | 204 |
| Bad input (client's fault, fixable) | 400 |
| Missing or invalid auth token | 401 + `WWW-Authenticate: Bearer` |
| Auth valid, permission denied | 403 |
| Resource not found | 404 |
| Conflict (duplicate, stale version) | 409 |
| Pydantic / schema validation failure | 422 (FastAPI automatic) |
| Rate limit exceeded | 429 + `Retry-After` header |
| Server error | 500 |

## 401 vs 403
- **401**: could not identify the caller. Token missing, expired, or malformed. Always add `WWW-Authenticate: Bearer`.
- **403**: caller identified, but lacks permission. Don't reveal *why* if that leaks data.

Never return 200 for an error. Never return 404 when you mean 403 (leaks existence of the resource to unauthorized callers — unless hiding existence is the requirement).

## 400 vs 422
FastAPI automatically returns 422 for Pydantic validation failures. Reserve 400 for business-logic rejections where the input was syntactically valid but semantically wrong (e.g. `start_date > end_date`, referencing a non-existent foreign key).

```python
if body.start_date > body.end_date:
    raise HTTPException(status_code=400, detail="start_date must be before end_date")
```

## 409 Conflict
Use for duplicate creation or optimistic-lock violations:

```python
existing = await db.scalar(select(User).where(User.email == body.email))
if existing:
    raise HTTPException(status_code=409, detail="email already registered")
```

For optimistic locking include the expected version in the 409 detail so the client knows what to re-fetch.

## 204 No Content
Use for DELETE and for PATCH/PUT when you have nothing useful to return. Do NOT return `{}` or `{"ok": true}` — use 204 with an empty body.
