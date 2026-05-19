# ADR Bootstrap Detection Patterns

Scan the codebase (depth 3 from src_root) for the following patterns.
Build a `candidates` list. Each candidate has: `title`, `evidence`, `proposed_consequence`.

Filter to candidates with clear evidence (at least 2 matching files or a pubspec.yaml entry).
Discard candidates with only 1 ambiguous match.

## Python patterns

| What to grep/find | Candidate title | Proposed consequence |
|---|---|---|
| `import fastapi` / `from fastapi` | API Framework: FastAPI | All new routes must use FastAPI routers |
| `import flask` / `from flask` | API Framework: Flask | All new routes must use Flask blueprints |
| `import django` / `DJANGO_SETTINGS_MODULE` | API Framework: Django | All new views must follow Django patterns |
| `from sqlalchemy` / `import sqlalchemy` | Database Access: SQLAlchemy ORM | All DB access must go through SQLAlchemy models |
| `import psycopg2` / `import asyncpg` (no ORM) | Database Access: Raw SQL | All queries must use parameterised statements |
| `import httpx` / `import requests` / `aiohttp` | HTTP Client Library | All outbound HTTP must use this library |
| `conftest.py` / `pytest.ini` / `pyproject` pytest | Test Framework: pytest | All tests must use pytest conventions |
| `import unittest` (no pytest config found) | Test Framework: unittest | All tests must use unittest.TestCase |
| dirs named `repositories/` or `repos/` | Repository Pattern for DB Access | All DB access must go through repository classes |
| `jwt` / `passlib` / `bcrypt` / `oauth` | Authentication Approach | All auth must use this library consistently |

## Flutter/Dart patterns

| What to grep/find | Candidate title | Proposed consequence |
|---|---|---|
| `flutter_bloc:` or `bloc:` in pubspec.yaml | State Management: BLoC | All screens must use BLoC for state |
| `riverpod:` / `flutter_riverpod:` in pubspec | State Management: Riverpod | All screens must use Riverpod providers |
| `provider:` in pubspec.yaml | State Management: Provider | All screens must use Provider for state |
| `get:` in pubspec.yaml | State Management: GetX | All screens must use GetX controllers |
| `go_router:` in pubspec.yaml | Navigation: GoRouter | All navigation must use GoRouter |
| `auto_route:` in pubspec.yaml | Navigation: AutoRoute | All navigation must use AutoRoute |
| `Navigator.push` / `Navigator.pushNamed` | Navigation: Manual Navigator | All navigation must use Navigator directly |
| `dio:` in pubspec.yaml | HTTP Client: Dio | All HTTP calls must use Dio |
| `http:` in pubspec.yaml (no dio) | HTTP Client: dart:http package | All HTTP calls must use the http package |
| `hive:` / `isar:` / `sqflite:` in pubspec.yaml | Local Storage Strategy | All local persistence must use this library |
| dirs named `/domain/` `/data/` `/presentation/` | Clean Architecture Layer Structure | All new code must follow clean architecture layers |
| `ConsumerWidget` / `ConsumerStatefulWidget` | All screens must use ConsumerWidget | No new StatefulWidget screens — use ConsumerWidget |
