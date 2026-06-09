# Pydantic v2 Patterns

## model_config replaces class Config

```python
# WRONG — v1 style (deprecated, emits warnings)
class User(BaseModel):
    class Config:
        orm_mode = True
        allow_population_by_field_name = True
        schema_extra = {"example": {"name": "Alice"}}

# CORRECT — v2
from pydantic import BaseModel, ConfigDict

class User(BaseModel):
    model_config = ConfigDict(
        from_attributes=True,           # was: orm_mode = True
        populate_by_name=True,          # was: allow_population_by_field_name
        json_schema_extra={"example": {"name": "Alice"}},  # was: schema_extra
        validate_assignment=True,
    )
```

Key renames: `orm_mode` → `from_attributes`, `allow_population_by_field_name` → `populate_by_name`, `schema_extra` → `json_schema_extra`, `validate_all` → `validate_default`.

## @field_validator replaces @validator

```python
# WRONG — v1 (@classmethod missing, old decorator)
from pydantic import validator
class Product(BaseModel):
    name: str
    @validator('name')
    def name_not_empty(cls, v): return v.strip()

# CORRECT — v2 (@classmethod is REQUIRED — AI often omits it)
from pydantic import field_validator, ValidationInfo

class Product(BaseModel):
    name: str
    password: str
    password_confirm: str

    @field_validator('name')
    @classmethod
    def name_not_empty(cls, v: str) -> str:
        if not v.strip():
            raise ValueError('name cannot be empty')
        return v.strip()

    @field_validator('password_confirm')
    @classmethod
    def passwords_match(cls, v: str, info: ValidationInfo) -> str:
        if 'password' in info.data and v != info.data['password']:
            raise ValueError('passwords do not match')
        return v
```

## @model_validator replaces @root_validator

```python
from pydantic import model_validator
from typing_extensions import Self

class DateRange(BaseModel):
    start: date
    end: date

    @model_validator(mode='after')          # instance method, all fields set
    def start_before_end(self) -> Self:
        if self.start > self.end:
            raise ValueError('start must be before end')
        return self

    @model_validator(mode='before')         # class method, raw input dict
    @classmethod
    def normalize(cls, data: Any) -> Any:
        if isinstance(data, dict) and 'name' in data:
            data['name'] = data['name'].strip()
        return data
```

## Method renames (all v1 methods still work but emit deprecation warnings)

```python
obj.dict()           → obj.model_dump()
obj.json()           → obj.model_dump_json()
obj.parse_obj(data)  → obj.model_validate(data)
obj.parse_raw(s)     → obj.model_validate_json(s)
obj.schema()         → obj.model_json_schema()
obj.construct()      → obj.model_construct()
obj.copy()           → obj.model_copy()
parse_obj_as(T, d)   → TypeAdapter(T).validate_python(d)
```

## Optional[X] behavior change — silent breaking change

```python
# v2: Optional[str] is required (nullable but no default)
class Model(BaseModel):
    name: Optional[str]      # raises ValidationError if omitted

# CORRECT
class Model(BaseModel):
    name: Optional[str] = None
    name: str | None = None  # Python 3.10+ equivalent
```

## Computed fields

```python
from pydantic import computed_field
from functools import cached_property

class User(BaseModel):
    first_name: str
    last_name: str

    @computed_field        # return type annotation IS required
    @property
    def full_name(self) -> str:
        return f"{self.first_name} {self.last_name}"
```

## BaseSettings moved to pydantic-settings

```python
# WRONG — from pydantic import BaseSettings
from pydantic_settings import BaseSettings, SettingsConfigDict  # pip install pydantic-settings

class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file='.env', env_file_encoding='utf-8')
    database_url: str
    debug: bool = False
```

## Generic models — GenericModel removed

```python
# WRONG — from pydantic.generics import GenericModel
from typing import Generic, TypeVar
from pydantic import BaseModel

T = TypeVar('T')
class Response(BaseModel, Generic[T]):   # no GenericModel needed
    data: T
    count: int
```

## AI mistake cheatsheet

| AI generates | Correct |
|---|---|
| `@validator('x')` | `@field_validator('x')` + `@classmethod` |
| `@field_validator` without `@classmethod` | Always pair them |
| `@root_validator` | `@model_validator(mode='after')` |
| `class Config: orm_mode = True` | `model_config = ConfigDict(from_attributes=True)` |
| `.dict()`, `.json()`, `.parse_obj()` | `.model_dump()`, `.model_dump_json()`, `.model_validate()` |
| `field: Optional[str]` (no default) | `field: Optional[str] = None` |
| `from pydantic import BaseSettings` | `from pydantic_settings import BaseSettings` |
| `from pydantic.generics import GenericModel` | `class M(BaseModel, Generic[T])` |
