# Cross-Stack Type Mapping — FastAPI ↔ Flutter

Used by the planner, implementer, and reviewer whenever `config.profiles`
contains both a backend profile (`fastapi` or `python`) AND `flutter`.

---

## Type Mapping (Pydantic → Dart)

| Python / Pydantic type | Dart type | Notes |
|---|---|---|
| `str` | `String` | |
| `int` | `int` | |
| `float` | `double` | |
| `bool` | `bool` | |
| `bytes` | `Uint8List` | import dart:typed_data |
| `datetime` | `DateTime` | JSON: ISO 8601 string; use `DateTime.parse()` |
| `date` | `DateTime` | date-only; strip time in Flutter |
| `UUID` | `String` | Flutter receives as string, no uuid type needed |
| `Decimal` | `double` | precision loss possible — flag as WARN if currency |
| `Optional[X]` | `X?` | nullable; Dart null-safety enforced |
| `X \| None` | `X?` | Pydantic v2 union-with-None — same as Optional |
| `List[X]` | `List<X>` | |
| `Set[X]` | `List<X>` | Dart has no Set in JSON; dedup in Flutter if needed |
| `Dict[str, X]` | `Map<String, X>` | |
| `Dict[str, Any]` | `Map<String, dynamic>` | avoid — prefer typed models |
| `Any` | `dynamic` | WARN — should be typed |
| `None` (return) | `void` / omit field | endpoint returns no body |
| `Enum` subclass | `enum` or `String` | match the enum values exactly |

### Nullable mismatch is a MAJOR finding
A field that is `Optional[str]` in Pydantic but `String` (non-nullable) in Dart
will crash at runtime when the API returns `null`. This is the most common
cross-stack bug. Always flag type-optional/non-optional mismatches as MAJOR.

---

## Naming Conventions

### Schema → Model file

| FastAPI class name | Expected Dart file | Expected Dart class |
|---|---|---|
| `UserResponse` | `lib/models/user_model.dart` | `UserModel` |
| `CreateUserRequest` | `lib/models/create_user_request.dart` | `CreateUserRequest` |
| `UpdateUserRequest` | `lib/models/update_user_request.dart` | `UpdateUserRequest` |
| `UserListResponse` | `lib/models/user_model.dart` | returns `List<UserModel>` |
| `PaginatedResponse[User]` | `lib/models/paginated_response.dart` | `PaginatedResponse<UserModel>` |
| `TokenResponse` | `lib/models/token_model.dart` | `TokenModel` |
| `ErrorResponse` | `lib/models/error_model.dart` | `ErrorModel` |

**General rules:**
- `XxxResponse` → `xxx_model.dart`, class `XxxModel`
- `XxxRequest` → `xxx_request.dart`, class `XxxRequest`
- Strip `Create`/`Update`/`Delete` suffix for simple models if Flutter uses a single class
- `PaginatedResponse[X]` → generic wrapper `PaginatedResponse<XModel>`

If the project doesn't follow these conventions, check `lib/models/` glob and
SRS.md `## 5. Data Models` for the actual class names. Do not invent a mapping
that isn't confirmed by the codebase.

---

## Service Method Conventions

| FastAPI endpoint | Expected Flutter service method |
|---|---|
| `GET /api/users` | `getUsers()` → `Future<List<UserModel>>` |
| `GET /api/users/{id}` | `getUserById(String id)` → `Future<UserModel>` |
| `POST /api/users` | `createUser(CreateUserRequest req)` → `Future<UserModel>` |
| `PUT /api/users/{id}` | `updateUser(String id, UpdateUserRequest req)` → `Future<UserModel>` |
| `DELETE /api/users/{id}` | `deleteUser(String id)` → `Future<void>` |
| `POST /api/auth/login` | `login(String email, String password)` → `Future<TokenModel>` |
| `POST /api/auth/refresh` | `refreshToken(String token)` → `Future<TokenModel>` |

Service files live in `lib/services/` and are named `{domain}_service.dart`.

---

## JSON Field Name Conventions

FastAPI defaults to snake_case JSON keys when using Pydantic's default serialization.
Flutter `fromJson` / `toJson` methods must use the same key names.

Common conflict: Pydantic `model_config = ConfigDict(alias_generator=to_camel)` changes
all keys to camelCase. Check `src/core/config.py` or the schema's `model_config` before
assuming snake_case. Mismatched key names cause silent null deserialization in Flutter.

If the project uses camelCase aliases in Pydantic:
- Dart `fromJson` must use camelCase keys
- Flag any snake_case keys in Flutter models as MAJOR finding

---

## Error Response Contract

FastAPI's default `HTTPException` produces:
```json
{ "detail": "string" }
```

Pydantic validation errors produce:
```json
{ "detail": [ { "loc": [...], "msg": "...", "type": "..." } ] }
```

The Flutter `ErrorModel` must handle both shapes.
If `detail` is typed as `String` in Dart but the API returns a list on validation
errors: runtime crash. Check that `ErrorModel.detail` is `dynamic` or properly
typed as a union.

---

## Checklist for the Reviewer (Phase 6)

For each Pydantic schema in `## Files Changed`:

1. Derive the expected Dart model using naming conventions above
2. Check `lib/models/` for the file — missing = MAJOR
3. For each field in the Pydantic class:
   a. Check the field exists in the Dart class — missing = MAJOR
   b. Map the Python type to Dart type using the table above — mismatch = MAJOR
   c. Check nullable: `Optional[X]` must map to `X?` in Dart — mismatch = MAJOR
4. Extra fields in Dart not in Pydantic: WARN (may be UI-only computed fields)
5. For new endpoints: check for a corresponding Flutter service method — missing = WARN

For each new FastAPI endpoint in `## Files Changed`:

1. Check `lib/services/` for a method matching the endpoint's HTTP verb + path
2. Not found: WARN (Flutter side may be a separate feature)
3. Found: verify the return type matches the `response_model` (using type table)
