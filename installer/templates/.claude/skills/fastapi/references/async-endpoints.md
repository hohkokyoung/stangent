# Async endpoints

FastAPI executes `async def` path operations directly on the event loop. Sync `def` handlers run in a threadpool — that's fine for fully sync code but anti-pattern when mixed with async dependencies (you can't `await` from sync code).

## Rule
Default to `async def` for every route. Only use sync `def` when the entire dependency chain and handler body is sync, and you have a specific reason.

## Avoid blocking the loop
Any of the following inside an async handler will block the event loop and degrade throughput:
- `time.sleep` — use `await asyncio.sleep`.
- `requests.get` — use `httpx.AsyncClient`.
- Filesystem reads of large files — use `aiofiles` or move to threadpool with `asyncio.to_thread(open, ...)`.
- Pure-CPU work over a few ms — `await asyncio.to_thread(...)`.

## Example
```python
from fastapi import APIRouter
import httpx

router = APIRouter()

@router.get("/health")
async def health(client: httpx.AsyncClient = Depends(get_http_client)) -> dict:
    r = await client.get("https://example.com/up")
    return {"upstream": r.status_code}
```

## Long-running work
For >5s work, return 202 with a job id and process in a background worker (arq, dramatiq, celery+async-bridge). Do not block a request.
