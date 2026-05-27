# Depends patterns

## DB session per request
```python
async def get_db() -> AsyncIterator[AsyncSession]:
    async with SessionLocal() as session:
        yield session
```
Inject with `db: AsyncSession = Depends(get_db)`. Never instantiate `SessionLocal()` inside a handler.

## Settings
Singleton via `lru_cache`:
```python
@lru_cache
def get_settings() -> Settings:
    return Settings()  # pydantic-settings BaseSettings
```
Inject `settings: Settings = Depends(get_settings)`.

## Current user
```python
async def get_current_user(
    token: str = Depends(oauth2_scheme),
    db: AsyncSession = Depends(get_db),
) -> User:
    user = await users.from_token(db, token)
    if user is None:
        raise HTTPException(status_code=401, detail="invalid token")
    return user
```
Endpoints that require auth: `user: User = Depends(get_current_user)`.

## Sub-dependencies
Dependencies can themselves depend on other dependencies. Keep the graph shallow — one level of nesting is fine, three is a smell.

## Testing override
```python
app.dependency_overrides[get_db] = lambda: fake_session
```
Always clear in test teardown.
