# Cross-Stack Planning Scan

Only execute if `config.profiles` contains both a backend profile (`fastapi` or `python`) AND `flutter`.
Skip entirely for single-stack projects.

Read `.stangent/prompts/cross-stack-types.md` for naming conventions before starting.

## A — Route-to-service mapping

For each FastAPI route file the feature will touch (from Pass 3 or meta.md):
- Identify the HTTP endpoints being created or modified
- Grep `lib/services/` for existing calls to those endpoints
- Add any matching Flutter service file to `## Files to Touch` if not already listed
- If no Flutter service method exists for a new endpoint: add a note in `## Scope`:
  "New Flutter service method required: {ServiceClass}.{methodName}()"

## B — Schema-to-model mapping

For each Pydantic schema file the feature will touch (files in `src/schemas/`
or files containing `class.*BaseModel`):
- Derive the expected Dart model filename using cross-stack-types.md conventions
- Glob `lib/models/` for the file
- If it exists: add it to `## Files to Touch` (implementer must keep it in sync)
- If it does not exist yet: add a note in `## Scope`:
  "New Dart model required: {ModelName} in lib/models/{model_file}.dart"
  Add the new file path to `## Files to Touch`

## C — Breaking change flag

Check `SRS.md` `## 4. API Contracts` for any endpoint the feature changes.
If the endpoint is already documented and this feature modifies its request or response schema:
- Add to `## Scope`: "⚠ Breaking change to existing API contract:
  {METHOD} {path} — both FastAPI schema and Flutter model must be updated."
- Add the Flutter model file to `## Files to Touch` if not already there
