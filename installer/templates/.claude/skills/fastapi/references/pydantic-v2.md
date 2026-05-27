# Pydantic v2 essentials

## ConfigDict (replaces class Config)
```python
from pydantic import BaseModel, ConfigDict, Field
from typing import Annotated

class UserIn(BaseModel):
    name: Annotated[str, Field(min_length=1, max_length=50)]
    email: Annotated[str, Field(pattern=r"^[^@]+@[^@]+$")]
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)
```

## extra="forbid"
Reject unknown fields with 422. Always set on inbound request models.

## Discriminated unions
```python
from typing import Literal, Union
class Cat(BaseModel): kind: Literal["cat"]; meows: int
class Dog(BaseModel): kind: Literal["dog"]; barks: int
Pet = Annotated[Union[Cat, Dog], Field(discriminator="kind")]
```

## Validators
Prefer `Annotated[..., Field(...)]` for simple constraints. Use `@field_validator("name", mode="after")` for cross-field or computed logic. `mode="after"` runs against the parsed value.

## Response models
Separate from input models. Use `from_attributes=True` (v2 name for `orm_mode`) only on response models that wrap ORM rows.
```python
class UserOut(BaseModel):
    id: int
    name: str
    model_config = ConfigDict(from_attributes=True)
```

## Don't
- Don't use `class Config:` — that's v1 syntax.
- Don't share one model for both request input and DB persistence.
