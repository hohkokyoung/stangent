# SRS Extraction — API Contracts and Data Models

## Phase A — API Contract Extraction

### When to run

Check `api_extraction` field in the active profile.
Flutter: `api_extraction = false` — skip this phase entirely.
Python / FastAPI: proceed.

### What to extract

For each file in `## Files Changed` that is in a routes/api/views directory, read the file and extract:

**FastAPI:**
- Decorator: `@router.get("/path")`, `@app.post("/path")`, etc.
- Function signature: parameter names + types
- Pydantic models referenced as request/response body
- Response model if specified

**Flask:**
- `@app.route("/path", methods=[...])`, `@blueprint.route(...)`
- Parameters from function signature and `request.get_json()` usage

**Django:**
- URL patterns from `urls.py`
- View function/class parameters

### Output format

```
### {{METHOD}} {{path}}
**Feature:** {{feature_id}}
**Request:** { field: type, ... } | _none_
**Response:** { field: type, ... } | _see model below_
**Errors:** code — description | _standard HTTP_
```

Append under `## 4. API Contracts`. In update mode: find and replace the existing entry.

---

## Phase B — Data Model Extraction

### What to extract

For each file in `## Files Changed` tagged `[C]` (created) that contains a class, model, schema, or entity:

**Python:** look for Pydantic `BaseModel`, SQLAlchemy `Base`, dataclasses
**Flutter:** look for `class.*{`, `@freezed`, `@JsonSerializable`

Extract field names, types, and any constraints/annotations.

### Output format

```
### {{ModelName}} ({{feature_id}})
| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
```

Append under `## 5. Data Models`. In update mode: find and replace the existing entry.
