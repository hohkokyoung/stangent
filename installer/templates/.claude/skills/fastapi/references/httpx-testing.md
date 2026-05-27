# Testing FastAPI with httpx

## AsyncClient against the ASGI app
```python
import pytest
from httpx import AsyncClient, ASGITransport
from app.main import app

@pytest.mark.asyncio
async def test_health():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        r = await ac.get("/health")
    assert r.status_code == 200
```

## Override dependencies
```python
@pytest.fixture
def fake_user_dep():
    app.dependency_overrides[get_current_user] = lambda: User(id=1, email="t@e.co")
    yield
    app.dependency_overrides.clear()
```

## DB
Use a real test database (sqlite-in-memory for unit, postgres for integration). Wrap each test in a transaction and roll back, or truncate between tests. Don't mock the ORM — it gives false confidence.

## Test the boundary cases
For every route:
- 200/201 happy path
- 401 (missing token, invalid token)
- 422 (bad payload — extra field, missing required, out-of-range)
- 404 (not found)
- The specific edge_cases from the task file

## Don't
- Don't import `TestClient` (sync, threadpool surprises with async handlers).
- Don't `monkeypatch` internals when `dependency_overrides` is the supported API.
