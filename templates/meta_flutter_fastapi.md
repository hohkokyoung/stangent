# meta.md — Flutter + FastAPI starter
#
# Copy this file to .stangent/meta.md in your project and fill in your
# actual file paths. The planner reads this before writing any spec —
# when a file in the "When you touch" column is in scope, the listed
# dependent files are automatically appended to ## Files to Touch for review.
#
# This catches the most common cross-stack drift: you change a FastAPI
# schema or route and forget to update the Flutter service that calls it.
#
# HOW TO FILL THIS IN:
#   Left column  — glob patterns or exact paths in your repo
#   Right column — files/dirs the implementer must also review when left is touched
#
# Use globs for directories: src/routes/**  lib/services/**
# Use exact paths for high-risk files: src/core/security.py
# The [doc] prefix means "review only, do not write" — useful for docs and SRS.
#
# Delete rows that don't apply. Add rows for your own patterns.
# ─────────────────────────────────────────────────────────────────────────────

## Cascade Rules

| When you touch | Also review |
|---|---|
| `src/schemas/**` | `lib/models/` |
| `src/routes/**` | `lib/services/` |
| `src/routes/auth.py` | `lib/services/auth_service.dart`, `lib/screens/login_screen.dart`, `lib/screens/register_screen.dart` |
| `src/routes/users.py` | `lib/services/user_service.dart`, `lib/screens/profile_screen.dart` |
| `src/core/config.py` | `.env.example`, `lib/core/config.dart` |
| `src/core/security.py` | `lib/services/auth_service.dart` |
| `src/db/session.py` | `src/dependencies.py` |
| `src/models/**` | `src/schemas/**`, `[doc] .stangent/SRS.md` |
| `pubspec.yaml` | `src/requirements.txt` |
| `lib/services/**` | `src/routes/` |

---

## Supabase Cascade Rules (add these if using Supabase)
#
# Delete this section if your project does not use Supabase.
# These rules catch the most dangerous Supabase-specific drift patterns.

| When you touch | Also review |
|---|---|
| `supabase/migrations/**` | `[doc] .stangent/SRS.md` (schema section), `src/models/**`, `lib/models/` |
| `supabase/migrations/**` | RLS policy file if separate — verify every new table has RLS enabled |
| `src/core/security.py` | `supabase/migrations/` (check JWT algorithm or expiry hasn't drifted) |
| `supabase/seed.sql` | `tests/conftest.py` (test fixtures must match seed data shape) |
| `lib/core/config.dart` | `.env.example` (Supabase URL and anon key env var must be documented) |
| `lib/services/auth_service.dart` | `src/core/security.py`, `src/routes/auth.py` |
| `src/routes/auth.py` | `supabase/migrations/` (auth.users references, JWT config) |

---

## Notes

**Schema drift** — the highest-risk pattern in this stack.
When `src/schemas/user.py` (Pydantic) changes shape, `lib/models/user_model.dart`
must match. The cascade rule above surfaces this automatically — the reviewer
will see both files in scope and can verify field parity.

**Auth cascade** — `src/core/security.py` changes (token format, expiry, algorithm)
always ripple into `lib/services/auth_service.dart`. This is the most common
source of hard-to-debug 401 errors.

**Supabase migration cascade** — the most commonly missed pattern when using Supabase.
A migration that adds a column or table requires updating the Pydantic schema (FastAPI),
the Dart model (Flutter), AND verifying RLS policies still match the new shape.
The cascade rule surfaces all three automatically.

**Replacing this file** — run `/adr security-token-format` or similar to record
any auth contract decisions. ADRs enforce them going forward so the cascade
rule becomes a safety net rather than the primary check.
