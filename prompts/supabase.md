# Supabase Integration Guide

Used by the planner, implementer, and reviewer whenever `config.integrations.supabase.enabled = true`
or when Supabase is detected in the project (supabase-py in requirements.txt, supabase in pubspec.yaml).

---

## Architecture

### How to detect which architecture you are in

Read `config.profiles` from `.stangent/config.json`:

| Profiles present | Architecture |
|---|---|
| `flutter` only | Single-stack — Flutter talks directly to Supabase |
| `fastapi` + `flutter` | Double-stack — Flutter + FastAPI both use Supabase |
| `python` + `flutter` | Double-stack — FastAPI likely (check requirements.txt) |
| `fastapi` only | Server-only — FastAPI service wrapping Supabase PostgreSQL |

When double-stack: apply both Flutter and FastAPI Supabase checklists.
When single-stack (Flutter only): apply Flutter checklist only; skip FastAPI service_role rules.

Supabase projects typically use one of two architectures. Know which one you are in before writing any spec.

### Single-stack (Flutter + Supabase only)

```
Flutter  ──────────►  Supabase
                       ├── Auth (JWT)
                       ├── PostgreSQL (via RLS)
                       ├── Realtime
                       └── Storage
```

Flutter talks directly to Supabase. No backend server. Security entirely enforced by RLS policies.

### Double-stack (Flutter + FastAPI + Supabase)

```
Flutter
  ├──────────────────►  Supabase  (realtime, storage, simple CRUD on RLS-protected tables)
  └──────────────────►  FastAPI   (business logic, aggregations, 3rd-party integrations)
                              └──►  Supabase PostgreSQL  (service_role or direct PG connection)
```

Flutter may bypass FastAPI for simple reads/writes if RLS is sufficient. FastAPI handles
anything requiring server-side logic, secrets, or elevated privileges.

**Rule:** if a feature requires the `service_role` key, it MUST go through FastAPI.
Flutter NEVER holds the service_role key.

---

## Key Concepts

### Keys

| Key | Scope | Where it lives |
|---|---|---|
| `anon` | Public, RLS-restricted | Flutter env/config — safe to ship |
| `service_role` | Full DB bypass, no RLS | FastAPI `.env` only — NEVER in Flutter |
| JWT (user token) | Per-session, RLS context | Flutter memory (secure storage) |

The `anon` key is safe in Flutter because RLS policies block access to rows the user
doesn't own. The `service_role` key bypasses all RLS — it grants full database access.

### Row Level Security (RLS)

Every table that stores user data MUST have RLS enabled. Without it, any `anon` key holder
can read all rows.

Common patterns:
```sql
-- Users can only access their own rows
CREATE POLICY "user_owns_row" ON table_name
  FOR ALL USING (auth.uid() = user_id);

-- Public read, authenticated write
CREATE POLICY "public_read" ON table_name FOR SELECT USING (true);
CREATE POLICY "auth_write"  ON table_name FOR INSERT WITH CHECK (auth.role() = 'authenticated');
```

### Auth Flow

Supabase uses JWT. The user signs in via Supabase Auth → receives `access_token` + `refresh_token`.

**Flutter:**
- `Supabase.instance.client.auth.signIn(...)` → stores session automatically
- Access token available at `Supabase.instance.client.auth.currentSession?.accessToken`
- Listen to auth changes: `Supabase.instance.client.auth.onAuthStateChange`

**FastAPI:**
- Receive `Authorization: Bearer <access_token>` from Flutter
- Verify against Supabase JWKS or using `SUPABASE_JWT_SECRET`
- Extract `sub` (user UUID) and `role` from decoded payload
- Pass user context into DB queries (for audit, logging, or fine-grained logic)

---

## FastAPI + Supabase Patterns

### JWT Verification Middleware

```python
# src/core/security.py
import jwt
from fastapi import HTTPException, Security
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

security = HTTPBearer()

def verify_supabase_jwt(credentials: HTTPAuthorizationCredentials = Security(security)):
    token = credentials.credentials
    try:
        payload = jwt.decode(
            token,
            settings.SUPABASE_JWT_SECRET,
            algorithms=["HS256"],
            audience="authenticated",
        )
        return payload
    except jwt.ExpiredSignatureError:
        raise HTTPException(status_code=401, detail="Token expired")
    except jwt.InvalidTokenError:
        raise HTTPException(status_code=401, detail="Invalid token")
```

Use `Depends(verify_supabase_jwt)` on every protected route. Never copy-paste auth into handlers.

### Database Access

Two patterns for FastAPI → Supabase PostgreSQL:

**Option A — SQLAlchemy async (preferred for complex queries)**
```python
# Use the direct PostgreSQL connection string, not the REST API
# DATABASE_URL = postgresql+asyncpg://user:pass@db.xxx.supabase.co:5432/postgres
```

**Option B — supabase-py client (preferred for simple CRUD)**
```python
from supabase import create_client
supabase = create_client(settings.SUPABASE_URL, settings.SUPABASE_SERVICE_ROLE_KEY)
# service_role only on server — never returned to clients
```

Which pattern to use is an ADR decision. Check `decisions.md` before implementing.

### Environment Variables (FastAPI)

```
SUPABASE_URL=https://xxx.supabase.co
SUPABASE_SERVICE_ROLE_KEY=eyJ...   # NEVER commit, NEVER return in responses
SUPABASE_JWT_SECRET=your-jwt-secret
DATABASE_URL=postgresql+asyncpg://...  # direct PG connection (for SQLAlchemy)
```

Never log these. Never include them in error responses. Never return `SUPABASE_SERVICE_ROLE_KEY`
in any API response field, even in dev mode.

---

## Flutter + Supabase Patterns

### Initialization

```dart
// lib/main.dart — initialize once
await Supabase.initialize(
  url: AppConfig.supabaseUrl,      // from dart_dotenv or flavors
  anonKey: AppConfig.supabaseAnonKey,
);
```

Never hardcode URL or anon key in Dart source. Use a config/flavor system.

### Auth Token Storage

```dart
// Supabase Flutter SDK stores session in secure storage automatically.
// Do NOT manually copy tokens to SharedPreferences.
// Access via: Supabase.instance.client.auth.currentSession
```

### Realtime Subscriptions

```dart
// Always unsubscribe in dispose() to prevent memory leaks and ghost listeners
class _MyWidgetState extends State<MyWidget> {
  late final RealtimeChannel _channel;

  @override
  void initState() {
    super.initState();
    _channel = Supabase.instance.client
        .channel('table_name')
        .onPostgresChanges(...)
        .subscribe();
  }

  @override
  void dispose() {
    Supabase.instance.client.removeChannel(_channel);
    super.dispose();
  }
}
```

A realtime channel left open after widget disposal leaks a WebSocket connection
and triggers ghost updates on a dead widget.

### Storage (Private Buckets)

```dart
// Use signed URLs for private bucket objects — never expose service_role in Flutter
final response = await Supabase.instance.client.storage
    .from('private-bucket')
    .createSignedUrl('path/to/file.jpg', 3600); // 1h expiry
```

Public bucket objects can be served directly. Private bucket objects require
signed URLs generated either by Flutter (anon key, if policy allows) or FastAPI
(service_role key for unrestricted access).

---

## DBHub + Supabase

DBHub connects to Supabase via the direct PostgreSQL connection string (port 5432),
not the REST API. This gives the query_analyzer access to real schema, indexes,
and RLS policies.

In `config.json`:
```json
"integrations": {
  "dbhub": {
    "enabled": true,
    "mcp_server": "dbhub"
  },
  "supabase": {
    "enabled": true,
    "project_url": "https://xxx.supabase.co",
    "direct_connection": "postgresql://postgres:pass@db.xxx.supabase.co:5432/postgres"
  }
}
```

When `dbhub.enabled = true` and the feature touches DB tables, the planner calls
`mcp__{mcp_server}__search_objects` to retrieve live schema instead of inferring
from migration files.

---

## Security Rules for All Agents

These rules apply wherever Supabase is in use. Violations are always CRITICAL or MAJOR.

| Rule | Severity | Who checks |
|---|---|---|
| `service_role` key in Flutter/Dart code | CRITICAL | reviewer, security scanner |
| `service_role` key returned in any API response | CRITICAL | reviewer, security scanner |
| New table without RLS enabled | MAJOR | reviewer (FastAPI or migration files) |
| Auth bypass — route without JWT verification in FastAPI | MAJOR | reviewer |
| Hardcoded `SUPABASE_URL` or `SUPABASE_ANON_KEY` in Dart source | MAJOR | reviewer |
| Realtime channel not unsubscribed in dispose() | MAJOR | reviewer (Flutter) |
| `anon` key used server-side for service_role operations | MAJOR | reviewer (FastAPI) |
| Auth tokens stored in SharedPreferences (not secure storage) | MAJOR | reviewer (Flutter) |
| Signed URL not used for private storage bucket access in Flutter | WARN | reviewer |
| `SUPABASE_JWT_SECRET` logged or echoed in any handler | CRITICAL | security scanner |

---

## Migration Files

Supabase migrations live in `supabase/migrations/`. When a feature adds a migration:
- Check the migration enables RLS on any new table
- Check the migration creates the correct RLS policies (not just `ENABLE ROW LEVEL SECURITY`)
- Check that foreign keys reference `auth.users(id)` not a custom users table (unless ADR says otherwise)
- Verify `supabase/migrations/` is in `## Files Changed`

A migration that creates a table but forgets `ALTER TABLE ... ENABLE ROW LEVEL SECURITY`
is a MAJOR finding — the table is publicly readable by any anon key holder.
