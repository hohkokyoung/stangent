# SKILL: fastapi

## Purpose
Governs how the implementer writes FastAPI endpoints, dependencies, and request/response models. Scope: HTTP layer of a FastAPI service (routers, Depends, Pydantic v2, async I/O, error responses). Does NOT cover database schema design, deploy, or non-HTTP concerns.

## Rules

1. **Always async.** All path operations are `async def`. Any sync blocking call inside an async path operation must be wrapped in `asyncio.to_thread` or moved out.
2. **Inject everything via `Depends`.** No globals for DB sessions, settings, current user, or auth. Make dependencies typed.
3. **Pydantic v2 only.** Use `BaseModel`, `Field`, `model_config = ConfigDict(...)`. No `class Config` (v1 style). Validate via type hints; prefer `Annotated[T, Field(...)]` for constraints.
4. **Explicit status codes.** Set `status_code=` on each route. Use `HTTPException` with status + detail for failure paths. Never raise bare `Exception` from a handler.
5. **Strict separation of request, response, and persistence models.** Persistence models stay out of the HTTP layer.
6. **Auth via dependency.** A `get_current_user` style dependency returns the authenticated principal or raises `HTTPException(401)`. Endpoints declare it via `Depends`.
7. **422 is automatic for body validation failures.** Do not catch `RequestValidationError` to remap it unless there is a documented reason.
8. **No sync file or network I/O in route handlers.** Use `httpx.AsyncClient` (one shared client, injected) for outbound HTTP.

## Patterns

- **Endpoint shape:**
  ```python
  @router.put("/users/me", status_code=200, response_model=UserOut)
  async def update_me(
      payload: UserUpdateIn,
      user: User = Depends(get_current_user),
      db: AsyncSession = Depends(get_db),
  ) -> UserOut:
      ...
  ```
- **Dependency for DB session:** an `async def get_db()` that yields an `AsyncSession` and closes it.
- **Error mapping:** domain errors → `HTTPException(status_code=..., detail=...)` at the route boundary; never let them bubble as 500s.
- **Pydantic v2 model:**
  ```python
  class UserUpdateIn(BaseModel):
      name: Annotated[str, Field(min_length=1, max_length=50)]
      avatar_url: Annotated[str | None, Field(default=None)]
      model_config = ConfigDict(extra="forbid")
  ```

## Anti-patterns

- `def` instead of `async def` on a route handler.
- Reading settings via `os.environ[...]` directly inside a handler.
- Returning ORM objects directly (leaks schema, lazy-loads at serialization time).
- Catching `RequestValidationError` to issue a custom 400 — that's the framework's job.
- Sharing `httpx.Client` (sync) in async code.
- Mixing path params and body in a single Pydantic model.
- Using `class Config` (v1) — must be `model_config = ConfigDict(...)`.
