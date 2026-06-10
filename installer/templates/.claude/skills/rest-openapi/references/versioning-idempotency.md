# Versioning and Idempotency

## URL versioning

Prefix breaking changes with `/v2/`. Non-breaking additions (new optional fields, new endpoints) don't need a version bump.

```python
# main.py
from app.routes import users_v1, users_v2

app.include_router(users_v1.router, prefix="/v1/users")
app.include_router(users_v2.router, prefix="/v2/users")
```

What counts as breaking:
- Removing a field from a response
- Changing a field's type
- Making an optional field required
- Changing HTTP status codes on existing paths
- Changing authentication requirements

What is non-breaking (no version bump needed):
- Adding new optional fields to responses
- Adding new endpoints
- Adding new optional query params

## Deprecation

Mark old routes in OpenAPI so clients have a heads-up before removal:

```python
@router.get("/users/{id}", deprecated=True, response_model=UserOutV1)
async def get_user_v1(id: int): ...
```

Add a `Sunset` header on deprecated responses:

```python
from fastapi import Response

@router.get("/{id}", deprecated=True)
async def get_user_v1(id: int, response: Response):
    response.headers["Sunset"] = "Sat, 01 Jan 2026 00:00:00 GMT"
    ...
```

## Idempotency keys

For non-idempotent POST operations that must not run twice (payments, emails, job dispatches):

```python
from fastapi import Header
import hashlib, json

@router.post("/charges", status_code=201, response_model=ChargeOut)
async def create_charge(
    body: ChargeCreate,
    idempotency_key: str | None = Header(default=None, alias="Idempotency-Key"),
    cache: Redis = Depends(get_redis),
):
    if idempotency_key:
        cache_key = f"idem:{hashlib.sha256(idempotency_key.encode()).hexdigest()}"
        cached = await cache.get(cache_key)
        if cached:
            return json.loads(cached)   # replay stored result

    result = await _do_charge(body)

    if idempotency_key:
        await cache.set(cache_key, result.model_dump_json(), ex=86400)  # 24h TTL

    return result
```

Client usage:
```http
POST /v1/charges
Idempotency-Key: charge-user-42-order-99-attempt-1
Content-Type: application/json
```

If the operation has no idempotency support and the client retries, document that risk explicitly in the API reference and in the Decisions log.

## Don'ts
- Don't version every endpoint — only those with breaking changes.
- Don't use query params for versioning (`/users?v=2`) — harder to route at the proxy level.
- Don't delete old versions before clients have migrated — use the `Sunset` header and give at least 6 months.
- Don't store raw idempotency keys — hash them first (prevents timing attacks on cache lookups).
