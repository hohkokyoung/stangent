# Cross-Stack Drift Check (Reviewer)

Only execute if `config.profiles` contains both a backend profile (`fastapi` or `python`) AND `flutter`.
Skip entirely for single-stack projects.

Read `.stangent/prompts/cross-stack-types.md` for type mapping and naming conventions before starting.

## 6a — Schema → Model field parity

For each file in `## Files Changed` that is a Pydantic schema
(path contains `schemas/` OR file contains `class.*BaseModel`):

i.  Derive the expected Dart model file using naming conventions in cross-stack-types.md.
ii. Glob `lib/models/` for the file.
    - Not found → MAJOR: `{SchemaClass} has no corresponding Dart model —
      create lib/models/{model_file}.dart`

iii. For each field in the Pydantic class:
     - Map the Python type to its Dart equivalent using the type table.
     - Check the Dart class has a field with the same name.
       Missing field → MAJOR: `{DartModel}.{fieldName} missing — Pydantic has {fieldName}: {type}`
     - Check the Dart field type matches the mapped type.
       Type mismatch → MAJOR: `{DartModel}.{fieldName} is {dartType},
       expected {mappedType} (from Pydantic {pythonType})`
     - Check nullability: `Optional[X]` or `X | None` in Pydantic → `X?` in Dart.
       Non-nullable Dart field for nullable Pydantic field → MAJOR:
       `{DartModel}.{fieldName} is non-nullable but API may return null —
       runtime crash when backend returns null`

iv. Extra fields in Dart not present in Pydantic → WARN:
    `{DartModel}.{fieldName} has no Pydantic counterpart — UI-only field or stale`

## 6b — JSON key casing

Check the FastAPI project for `alias_generator` in `src/core/config.py` or
the schema's `model_config = ConfigDict(alias_generator=...)`.

- `to_camel` alias → JSON keys are camelCase → Dart `fromJson` must use camelCase.
  If Dart uses snake_case keys → MAJOR: `JSON key casing mismatch —
  FastAPI returns camelCase but Dart model uses snake_case keys`
- No alias → JSON keys are snake_case → Dart `fromJson` must use snake_case.
  If Dart uses camelCase keys → MAJOR: same as above, reversed.

## 6c — New endpoint → Flutter service method

For each [C] file in `## Files Changed` that contains a new FastAPI route
(`@router.get`, `@router.post`, etc.):

- Derive the expected Flutter service method using conventions in cross-stack-types.md.
- Grep `lib/services/` for the method.
  - Not found → WARN: `New endpoint {METHOD} {path} has no Flutter service method —
    add to lib/services/{domain}_service.dart or create a follow-up feature`
  - Found → check return type matches the endpoint's `response_model` (type table).
    Mismatch → MAJOR: `{ServiceClass}.{method}() returns {dartType},
    expected {mappedType} based on response_model={PydanticModel}`

## 6d — Error response contract

Check that `lib/models/error_model.dart` (or equivalent) handles both FastAPI
error shapes (`{"detail": "string"}` and `{"detail": [...]}` for validation errors).
If `detail` is typed as non-nullable `String` in Dart → WARN: potential crash on
Pydantic validation error responses.

## Output

Add all findings to `## Review Checklist` under a "Cross-Stack Consistency" section.
Severity: MAJOR for type/field mismatches and missing models. WARN for extras and missing service methods.
Promote any CRITICAL security findings to `## Review Verdict`.
