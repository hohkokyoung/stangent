# SKILL: rest-openapi

## Purpose
Governs how the implementer designs and implements REST APIs, including OpenAPI schema documentation. Scope: HTTP resource design, status codes, request/response shapes, auth headers, pagination, idempotency, and versioning. Works alongside `fastapi` (Python) or any server skill — this skill owns the API contract; the server skill owns the framework mechanics.

## Rules

1. **Resource paths are nouns; HTTP methods are verbs.** `/users`, `/orders/{id}` — never `/getUsers`, `/createOrder`. Nested resources are fine up to two levels: `/users/{id}/orders`. Beyond that, flatten.
2. **HTTP status codes are the primary success/error signal.** 200 read, 201 created, 204 no-content (empty body), 400 bad request (caller's fault, fixable), 401 unauthenticated, 403 forbidden (authenticated but lacks permission), 404 not found, 409 conflict (duplicate, stale version), 422 validation error, 500 server error. Never return 200 with a body that signals failure.
3. **Every error response has at least a `detail` field.** Optionally add a machine-readable `code` slug for client branching. No stack traces in production responses.
4. **Auth goes in the `Authorization: Bearer <token>` header — never in URL query params.** URL params are logged by proxies and appear in browser history. API keys follow the same rule.
5. **All list endpoints are paginated.** Default: cursor-based (no OFFSET scans on large tables). Expose `limit` (default 20, max 100). Response includes `next_cursor` (null = last page) and optionally `total`. Never return an unbounded list.
6. **PUT is a full replacement, PATCH is partial.** If you only need partial update, use PATCH. If the client sends all fields each time, use PUT. Never mix semantics on the same path.
7. **Non-idempotent POSTs that matter (payments, sends, deploys) accept an `Idempotency-Key` header.** Store key + result; replay the stored result on duplicate. If you can't implement idempotency, document the risk explicitly in the Decisions log.
8. **Version breaking changes via URL prefix** (`/v1/`, `/v2/`). Additive changes (new optional fields, new endpoints) are non-breaking and do not require a new version. Removing fields, changing types, and changing semantics are breaking.
9. **Every request/response body is typed with a schema** (Pydantic model, TypeScript interface, JSON Schema — whatever the framework provides). No `dict`, `any`, or raw `object` return types. OpenAPI docs must be derivable without manual annotation.
10. **Validate all input server-side.** Never trust client-supplied IDs as proof of access — enforce ownership (`WHERE id = $1 AND owner_id = current_user`) on every DB query. See `owasp` skill for injection and auth rules.

## Patterns

### Error envelope
```json
{ "detail": "User not found", "code": "user_not_found" }
```
`code` is optional but strongly recommended for client-side branching. Do NOT add `success: false` or wrap data in `{"data": {...}}` — keep it flat.

### Paginated list response
```json
{
  "items": [...],
  "next_cursor": "eyJpZCI6NDJ9",
  "total": 150
}
```
`total` is optional (skip if it requires a COUNT that's too expensive). `next_cursor: null` means last page.

### FastAPI route with correct status codes
```python
from fastapi import APIRouter, HTTPException, status
from pydantic import BaseModel

router = APIRouter(prefix="/v1/users", tags=["users"])

class UserOut(BaseModel):
    id: int
    display_name: str
    email: str

@router.get("/{user_id}", response_model=UserOut, status_code=status.HTTP_200_OK)
async def get_user(user_id: int, db: AsyncSession = Depends(get_db)):
    user = await db.get(User, user_id)
    if not user:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="User not found")
    return user

@router.post("/", response_model=UserOut, status_code=status.HTTP_201_CREATED)
async def create_user(body: UserCreate, db: AsyncSession = Depends(get_db)):
    ...
```

### Cursor pagination (FastAPI)
```python
from base64 import b64encode, b64decode
import json

@router.get("/", response_model=PaginatedUsers)
async def list_users(
    limit: int = Query(default=20, le=100),
    cursor: str | None = Query(default=None),
):
    after_id = json.loads(b64decode(cursor))["id"] if cursor else 0
    rows = await db.execute(
        select(User).where(User.id > after_id).order_by(User.id).limit(limit + 1)
    )
    items = rows.scalars().all()
    next_cursor = None
    if len(items) > limit:
        items = items[:limit]
        next_cursor = b64encode(json.dumps({"id": items[-1].id}).encode()).decode()
    return {"items": items, "next_cursor": next_cursor}
```

### Idempotency key (FastAPI)
```python
@router.post("/charges", status_code=status.HTTP_201_CREATED)
async def create_charge(
    body: ChargeCreate,
    idempotency_key: str | None = Header(default=None, alias="Idempotency-Key"),
):
    if idempotency_key:
        cached = await cache.get(f"idem:{idempotency_key}")
        if cached:
            return cached
    result = await _do_charge(body)
    if idempotency_key:
        await cache.set(f"idem:{idempotency_key}", result, ex=86400)
    return result
```

## Planner hints

Before finalising task decomposition, check for API-surface gaps:
- Does any list endpoint lack a pagination story? Returning all rows is a ticking time bomb.
- Does this change an existing endpoint's response shape (field removed, type changed, semantic changed)? That's a breaking change — flag versioning.
- Does any endpoint accept user-supplied URLs, file paths, or redirect targets? SSRF / open redirect risk — see `owasp` skill.
- Does any POST create a resource that shouldn't be created twice on retry? Needs idempotency key.
- Is auth required? Confirm who can call each endpoint (anonymous, authenticated, owner-only, admin).

## Anti-patterns

- `GET /getUsers` or `POST /createUser` — HTTP method already carries the action.
- `return {"success": False, "error": "..."}` with status 200 — use 4xx/5xx.
- `Authorization` token in URL query param — logged by every proxy and browser.
- `def get_all_users() -> list[dict]` — no schema, no OpenAPI docs, no validation.
- No pagination on list endpoints — OOM in prod when the table grows.
- `OFFSET n` pagination on large tables — full table scan grows with page number; use cursors.
- `except Exception: pass` in route handlers — silent 500s with no logging.
