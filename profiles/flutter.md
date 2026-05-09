# Flutter Profile
> Stangent language profile for Flutter mobile projects.
> Satisfies all fields required by profiles/_base.md.
> Target: mobile (iOS + Android). Web/desktop excluded for now.

---

## Identity

```
name:             flutter
version:          1.0.0
file_extensions:  [".dart"]
src_root:         lib/
```

Detection trigger (checked by init.py):
- `pubspec.yaml` present

---

## Commands

```
lint:
  dart analyze --format=json > .stangent/lint_report.json 2>&1

lint_fix:
  dart fix --apply

format:
  dart format lib/ test/

test:
  flutter test --reporter=json > .stangent/test_report.json 2>&1

test_coverage:
  flutter test --coverage --reporter=json > .stangent/test_report.json 2>&1
  # coverage output: coverage/lcov.info

security_sast:
  dart pub global run dart_code_metrics:metrics analyze lib/ --reporter=json > .stangent/sast_report.json 2>&1

dep_audit:
  flutter pub outdated --json > .stangent/dep_audit.json 2>&1

secrets_scan:
  detect-secrets scan --all-files --exclude-files "\.stangent/.*|coverage/.*|\.dart_tool/.*" > .stangent/secrets_report.json
```

---

## Config Detection

```
lint_config_files:
  - analysis_options.yaml   # primary Flutter/Dart lint config

test_config_files:
  - pubspec.yaml            # dev_dependencies: flutter_test
  - test/                   # presence of test/ directory
```

If no `analysis_options.yaml` is found: generate one with sensible defaults
and show developer for confirmation before first lint run.

---

## Anchor Files (Pass 2 Codebase Reading)

```
anchor_files:
  - pubspec.yaml
  - pubspec.lock
  - lib/main.dart
  - lib/app.dart
  - lib/core/
  - lib/models/
  - lib/services/
  - lib/providers/
  - lib/repositories/
  - lib/screens/
  - lib/widgets/
  - lib/router/
  - lib/router.dart
  - lib/navigation/
  - lib/theme/
  - lib/constants/
  - lib/utils/
  - test/
  - analysis_options.yaml
```

---

## Exclude Dirs (Pass 1 Tree Scan)

```
exclude_dirs:
  - .dart_tool
  - .flutter-plugins
  - .flutter-plugins-dependencies
  - build
  - android
  - ios
  - linux
  - macos
  - web
  - windows
  - coverage
  - .git
  - .stangent
```

---

## Review Checklist

The reviewer works through this list in order. Mark [x] if true, [ ] false: reason.

```
review_checklist:
  1.  All acceptance criteria implemented and each has at least one widget/unit test
  2.  No logic in build() methods — build() calls other widgets, nothing else
  3.  No expensive operations (DB calls, HTTP, parsing) in build()
  4.  const constructors used wherever possible
  5.  ListView/GridView use .builder() for lists > 10 items
  6.  No unnecessary setState() calls — state changes are minimal and targeted
  7.  State management follows the project ADR (Riverpod / Provider / Bloc — check decisions.md)
  8.  No raw sqflite/Drift queries with string interpolation
  9.  All sqflite/Drift queries use whereArgs or parameterized form
  10. No hardcoded strings — all user-visible text uses i18n keys (if i18n_aware)
  11. No hardcoded colors, text styles, or sizes — use theme
  12. No hardcoded credentials, API keys, tokens, or secrets
  13. No hardcoded environment-specific URLs or base addresses
  14. Error states have UI representation (not silent failures)
  15. Loading states have UI representation (spinner, skeleton)
  16. No unused imports
  17. No commented-out code
  18. No dead widgets or unreachable branches
  19. Widgets are extracted into meaningful named components (not monolithic build())
  20. New environment variables / config values added to .env.example and constants file
  21. Files changed match ## Files to Touch (or ## Files Changed explains deviation)
  22. No code outside feature scope (check ## Out of Bounds)
```

---

## Query Patterns

### Danger Patterns (FAIL — blocks review)
```
danger_patterns:
  - 'db\.rawQuery\(["\'].*\$\{.*\}'
    # sqflite rawQuery with string interpolation

  - 'db\.rawInsert\(["\'].*\$\{.*\}'
    # sqflite rawInsert with string interpolation

  - 'db\.rawUpdate\(["\'].*\$\{.*\}'
    # sqflite rawUpdate with string interpolation

  - 'db\.rawDelete\(["\'].*\$\{.*\}'
    # sqflite rawDelete with string interpolation

  - '\.rawQuery\(["\'].*"\s*\+'
    # string concatenation in rawQuery

  - 'customStatement\(["\'].*\$\{.*\}'
    # Drift customStatement with interpolation
```

### Warning Patterns (WARN — human review required)
```
warn_patterns:
  - '\.rawQuery\('
    # any rawQuery — review parameterization

  - 'customStatement\('
    # Drift raw statement — review parameterization

  - 'FirebaseFirestore.*\.where\(.*\$\{'
    # Firestore query with interpolated value — review for injection

  - 'collection\(.*\)\.doc\(.*\$\{'
    # Firestore doc path with interpolation — verify sanitization
```

---

## Conventions

```
conventions:
  test_file_pattern:  *_test.dart
  test_dir:           test/
  commit_prefix:      feat | fix | test | refactor | docs | chore
  widget_test_dir:    test/widgets/
  unit_test_dir:      test/unit/
  integration_test_dir: integration_test/
```

---

## i18n

`i18n_aware: true`

When enabled, reviewer checks that all user-visible strings use the project's
i18n mechanism. Detect the mechanism from pubspec.yaml:
- `flutter_localizations` → check for `.arb` files and `AppLocalizations`
- `easy_localization` → check for key-based translation calls
- None found → flag as MINOR in review (no i18n system present)

---

## API Extraction

`api_extraction: false` — Flutter is a client. Document the service layer
method signatures instead (the contracts with the backend).

SRS agent documents:
- Service classes in `lib/services/`
- Repository classes in `lib/repositories/`
- Public method signatures that represent feature capabilities

---

## State Management Detection

Read `pubspec.yaml` and `decisions.md` to determine state management library.
Apply the correct patterns accordingly:

```
riverpod:   look for flutter_riverpod in pubspec.yaml
            providers in lib/providers/
            ConsumerWidget / ConsumerStatefulWidget usage

provider:   look for provider in pubspec.yaml
            ChangeNotifier / Provider.of usage

bloc:       look for flutter_bloc in pubspec.yaml
            Bloc / Cubit classes, BlocBuilder / BlocProvider usage
```

If none detected and no ADR exists: ASK_DEVELOPER before implementing any state.

---

## Default analysis_options.yaml (generated if none found)

```yaml
include: package:flutter_lints/flutter.yaml

analyzer:
  errors:
    missing_required_param: error
    missing_return: error
    dead_code: warning
    unused_import: warning
    unused_local_variable: warning

linter:
  rules:
    - always_use_package_imports
    - avoid_print
    - avoid_unnecessary_containers
    - prefer_const_constructors
    - prefer_const_declarations
    - prefer_final_fields
    - sized_box_for_whitespace
    - use_key_in_widget_constructors
```

---

## Tool Installation Check (run by init.py)

```
required_tools:
  - flutter     # flutter SDK in PATH
  - dart        # comes with flutter
  - detect-secrets  # pip install detect-secrets

optional_tools:
  - dart_code_metrics  # dart pub global activate dart_code_metrics
```
