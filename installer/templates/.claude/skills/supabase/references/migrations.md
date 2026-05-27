# Migrations

## Layout
```
supabase/migrations/
  20260527131455_init.sql
  20260528090000_add_profiles.sql
```
Timestamped filename. Forward-only. Never edit an already-applied migration — write a new one.

## Idempotency
Where possible, use `IF NOT EXISTS` / `IF EXISTS`:
```sql
CREATE TABLE IF NOT EXISTS public.profiles (...);
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
```

## RLS in the same migration
A migration that creates a user-data table should also enable RLS and add at least one policy in the same file. Otherwise there's a window where the table exists but is unprotected (or, on Supabase, totally inaccessible to clients).

## Local workflow
```
supabase migration new add_profiles
# edit the new file
supabase db reset      # local dev only — wipes & re-applies all migrations
supabase db push       # apply to linked remote (after review)
```

## Generated types
```
supabase gen types typescript --linked > src/types/supabase.ts
```
Regenerate after every migration to keep TS in sync.

## Don't
- Don't edit a committed migration. Write a follow-up.
- Don't `supabase db reset` on shared / production environments — it wipes data.
- Don't skip the RLS step "for now" — there is no "for now" with security.
