# OpenAPI Schemas with Pydantic

## Always use typed response models

```python
from pydantic import BaseModel, EmailStr, Field
from datetime import datetime

class UserOut(BaseModel):
    id: int
    display_name: str
    email: EmailStr
    created_at: datetime

    model_config = {"from_attributes": True}   # Pydantic v2: allows ORM → model

@router.get("/{user_id}", response_model=UserOut, status_code=200)
async def get_user(user_id: int, db: AsyncSession = Depends(get_db)):
    user = await db.get(User, user_id)
    if not user:
        raise HTTPException(404, "User not found")
    return user   # Pydantic serialises the ORM object automatically
```

Never use `-> dict` or `response_model=None` for documented endpoints — OpenAPI docs won't show the schema.

## Separate input and output models

Input models validate; output models control what's exposed. Never reuse the same model for both.

```python
class UserCreate(BaseModel):
    display_name: str = Field(min_length=1, max_length=50)
    email: EmailStr
    password: str = Field(min_length=8)

class UserOut(BaseModel):
    id: int
    display_name: str
    email: EmailStr
    # no password field — never expose it
```

## Optional fields and partial updates (PATCH)

```python
class UserPatch(BaseModel):
    display_name: str | None = Field(default=None, min_length=1, max_length=50)
    email: EmailStr | None = None

@router.patch("/{user_id}", response_model=UserOut)
async def patch_user(user_id: int, body: UserPatch, db: AsyncSession = Depends(get_db)):
    user = await db.get(User, user_id)
    if not user:
        raise HTTPException(404, "User not found")
    update_data = body.model_dump(exclude_unset=True)   # only fields the client sent
    for field, value in update_data.items():
        setattr(user, field, value)
    await db.commit()
    await db.refresh(user)
    return user
```

`exclude_unset=True` is critical — it distinguishes "client sent null" from "client didn't send this field."

## Documenting with Field and model docstrings

```python
class OrderOut(BaseModel):
    """A placed order."""
    id: int = Field(description="Unique order ID")
    status: Literal["pending", "shipped", "delivered"] = Field(
        description="Current fulfillment status"
    )
    total_cents: int = Field(description="Order total in smallest currency unit (cents)")
```

FastAPI uses these to populate the OpenAPI spec — no manual annotation needed.

## Don'ts
- `-> dict` — no schema generated.
- `Any` field types — defeats validation and docs.
- Reusing ORM models directly as response models — leaks internal fields, breaks `from_attributes`.
- `model_dump()` without `exclude_unset=True` on PATCH handlers — overwrites fields the client didn't touch.
