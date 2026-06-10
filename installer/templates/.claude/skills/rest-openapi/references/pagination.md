# Pagination

## Cursor-based (preferred for large tables)

No full-table scan. Stable under concurrent inserts. Use when the table can grow large.

```python
from base64 import b64encode, b64decode
import json
from fastapi import Query
from sqlalchemy import select

@router.get("/items", response_model=PagedItems)
async def list_items(
    limit: int = Query(default=20, ge=1, le=100),
    cursor: str | None = Query(default=None),
    db: AsyncSession = Depends(get_db),
):
    after_id = json.loads(b64decode(cursor))["id"] if cursor else 0
    rows = await db.scalars(
        select(Item)
        .where(Item.id > after_id)
        .order_by(Item.id)
        .limit(limit + 1)       # fetch one extra to detect next page
    )
    items = list(rows)
    next_cursor = None
    if len(items) > limit:
        items = items[:limit]
        next_cursor = b64encode(json.dumps({"id": items[-1].id}).encode()).decode()
    return {"items": items, "next_cursor": next_cursor}
```

Response shape:
```json
{
  "items": [...],
  "next_cursor": "eyJpZCI6NDJ9"   // null = last page
}
```

## Offset-based (simple, avoid on large tables)

Fine for small datasets or admin UIs where table size is bounded.

```python
@router.get("/items", response_model=PagedItems)
async def list_items(
    page: int = Query(default=1, ge=1),
    limit: int = Query(default=20, ge=1, le=100),
    db: AsyncSession = Depends(get_db),
):
    offset = (page - 1) * limit
    total = await db.scalar(select(func.count()).select_from(Item))
    rows = await db.scalars(select(Item).offset(offset).limit(limit))
    return {"items": list(rows), "total": total, "page": page, "limit": limit}
```

Response shape:
```json
{
  "items": [...],
  "total": 150,
  "page": 2,
  "limit": 20
}
```

## Pydantic response models

```python
from pydantic import BaseModel

class PagedItems(BaseModel):
    items: list[ItemOut]
    next_cursor: str | None = None   # cursor-based

class PagedItemsOffset(BaseModel):
    items: list[ItemOut]
    total: int
    page: int
    limit: int
```

## Don'ts
- Don't return unbounded lists (`select(Item)` with no `.limit()`).
- Don't use `OFFSET` on tables that grow into millions of rows — it scans from the start each time.
- Don't put cursor material in a plain integer — encode it so the shape can change without a client breaking.
