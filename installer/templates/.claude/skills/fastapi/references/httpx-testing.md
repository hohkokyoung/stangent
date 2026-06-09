# FastAPI Testing with httpx

## Lifespan and TestClient

```python
# WRONG — lifespan never runs at module level
client = TestClient(app)

# CORRECT — use as context manager so lifespan fires
def test_something():
    with TestClient(app) as client:
        response = client.get("/items/foo")
        assert response.status_code == 200

@pytest.fixture
def client():
    with TestClient(app) as c:
        yield c
```

## Async tests with httpx.AsyncClient

```python
# pip install httpx anyio pytest-anyio
from httpx import ASGITransport, AsyncClient

@pytest.mark.anyio
async def test_create_item():
    async with AsyncClient(
        transport=ASGITransport(app=app),
        base_url="http://test",
    ) as ac:
        response = await ac.post("/items/", json={"name": "Foo"})
    assert response.status_code == 201
```

**AsyncClient does NOT trigger lifespan.** Use `asgi-lifespan` when needed:

```python
# pip install asgi-lifespan
from asgi_lifespan import LifespanManager

@pytest.mark.anyio
async def test_with_lifespan():
    async with LifespanManager(app) as manager:
        async with AsyncClient(
            transport=ASGITransport(app=manager.app),
            base_url="http://test",
        ) as ac:
            response = await ac.get("/items/foo")
    assert response.status_code == 200
```

## Dependency overrides

```python
@pytest.fixture
def client():
    app.dependency_overrides[get_current_user] = lambda: User(id=1, username="test")
    with TestClient(app) as c:
        yield c
    app.dependency_overrides = {}  # MUST clear — contaminates other tests

# DB session override
@pytest.fixture
def client(test_db_session):
    def override_get_db():
        try:
            yield test_db_session
        finally:
            pass

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as c:
        yield c
    app.dependency_overrides = {}
```

## Lifespan pattern (replaces @app.on_event)

```python
# DEPRECATED — cannot coexist with lifespan=
@app.on_event("startup")
async def startup(): ...

# CORRECT
from contextlib import asynccontextmanager

@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.db = await create_db_pool()
    yield
    await app.state.db.close()

app = FastAPI(lifespan=lifespan)

@app.get("/items")
async def items(request: Request):
    db = request.app.state.db
```

## Annotated parameters — preferred since FastAPI 0.95

```python
from typing import Annotated

async def endpoint(
    q: Annotated[str | None, Query(max_length=50)] = None,
    limit: Annotated[int, Query(ge=1, le=100)] = 10,
    db: Annotated[AsyncSession, Depends(get_db)],
): ...

# Never put default= inside Query() when using Annotated
```

## AI mistake cheatsheet

| AI generates | Correct |
|---|---|
| `client = TestClient(app)` at module level | `with TestClient(app) as client:` |
| `AsyncClient(app=app)` | `AsyncClient(transport=ASGITransport(app=app), base_url="http://test")` |
| No `app.dependency_overrides = {}` teardown | Always clear in fixture teardown |
| `@app.on_event("startup")` | `FastAPI(lifespan=lifespan)` |
| `q: str = Query(default=None)` | `q: Annotated[str \| None, Query()] = None` |
