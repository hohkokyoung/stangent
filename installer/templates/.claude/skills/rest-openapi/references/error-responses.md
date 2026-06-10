# Error Responses

## Standard shape

FastAPI's default error envelope uses `detail`. Stick to it for consistency:

```json
{ "detail": "User not found" }
```

Add a machine-readable `code` field when clients need to branch on error type:

```json
{ "detail": "Email already registered", "code": "email_conflict" }
```

Never invent a second shape (e.g. `{"error": "..."}` in some routes, `{"detail": "..."}` in others).

## Returning a structured error in FastAPI

```python
from fastapi import HTTPException

raise HTTPException(status_code=404, detail="User not found")

# With machine-readable code — pass a dict as detail
raise HTTPException(
    status_code=409,
    detail={"message": "Email already registered", "code": "email_conflict"},
)
```

## Validation errors (422) — leave them alone

FastAPI automatically formats Pydantic validation failures:
```json
{
  "detail": [
    {
      "loc": ["body", "email"],
      "msg": "value is not a valid email address",
      "type": "value_error.email"
    }
  ]
}
```
Don't intercept `RequestValidationError` unless your API contract demands a different shape and you've decided to own that divergence.

## Global exception handler for domain errors

Map domain exceptions once at the app boundary instead of catching them in every route:

```python
from fastapi import Request
from fastapi.responses import JSONResponse

class ResourceNotFound(Exception):
    def __init__(self, resource: str, id: int):
        self.resource = resource
        self.id = id

@app.exception_handler(ResourceNotFound)
async def not_found_handler(request: Request, exc: ResourceNotFound) -> JSONResponse:
    return JSONResponse(
        status_code=404,
        content={"detail": f"{exc.resource} {exc.id} not found", "code": "not_found"},
    )
```

## Don'ts
- Don't return `{"success": false, "error": "..."}` with status 200.
- Don't include stack traces or internal paths in responses (log them, don't surface them).
- Don't swallow exceptions silently — unhandled exceptions should reach the 500 handler so monitoring sees them.
- Don't invent per-route error shapes; one envelope, everywhere.
