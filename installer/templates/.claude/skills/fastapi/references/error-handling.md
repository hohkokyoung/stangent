# Error handling

## HTTPException at the boundary
Domain code raises domain errors. The route handler maps them to HTTP:
```python
@router.get("/users/{uid}", response_model=UserOut)
async def get_user(uid: int, db: AsyncSession = Depends(get_db)) -> UserOut:
    try:
        return await users.get(db, uid)
    except UserNotFound:
        raise HTTPException(status_code=404, detail="user not found")
```

## Custom global handler (when needed)
Register a single handler per domain exception. Don't catch generic `Exception` and remap — let unhandled errors return 500 so monitoring sees them.

```python
@app.exception_handler(QuotaExceeded)
async def quota_handler(_req, exc: QuotaExceeded):
    return JSONResponse(status_code=429, content={"detail": str(exc)})
```

## 422 is automatic
Pydantic validation failures already produce 422 with field-by-field detail. Don't catch `RequestValidationError` to remap unless your API contract explicitly demands a different shape.

## 401 vs 403
- 401: missing or invalid auth (could not identify the user).
- 403: identified, but not allowed.

Always include a `WWW-Authenticate: Bearer` header on 401 for bearer-token APIs.

## Don't
- Don't return `{"error": ...}` in some routes and `{"detail": ...}` in others — pick one shape (default is `detail`).
- Don't swallow exceptions silently. Log and re-raise, or remap.
- Don't `print` errors. Use a configured logger.
