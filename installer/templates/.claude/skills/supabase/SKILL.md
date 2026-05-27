# SKILL: supabase

## Purpose
Governs Supabase usage: RLS policies, edge functions, realtime subscriptions, and migrations. Scope: Postgres-with-RLS and Supabase platform features. Does NOT cover generic SQL design or non-Supabase Postgres.

## Rules

1. **RLS is ON for every table that holds user data.** No exceptions. A migration that creates a user-data table without `ENABLE ROW LEVEL SECURITY` is invalid.
2. **Every RLS-enabled table has at least one explicit policy.** No table with RLS on and zero policies (which silently denies everything to authenticated clients).
3. **Use `auth.uid()` in policies, never trust client-supplied user_ids.** Policies must filter by `auth.uid()` for ownership.
4. **Service role bypasses RLS.** Service-role keys must never be shipped to clients (web, mobile). Edge functions and trusted backends only.
5. **Migrations are forward-only and idempotent.** Each migration file is timestamped under `supabase/migrations/`. Never edit a migration after it's been applied to a shared environment — write a new one.
6. **Edge functions are Deno + TypeScript.** Use `Deno.serve`, async handlers, typed request/response.
7. **Realtime: subscribe by primary key or by explicit filter.** No unfiltered table subscriptions in client code.
8. **No raw SQL execution from client code.** Only RPC functions exposed via PostgREST, with their own RLS-aware SECURITY INVOKER semantics.

## Patterns

- **RLS policy for owned rows:**
  ```sql
  CREATE POLICY "owner can read own profile"
    ON public.profiles
    FOR SELECT
    TO authenticated
    USING (auth.uid() = user_id);

  CREATE POLICY "owner can update own profile"
    ON public.profiles
    FOR UPDATE
    TO authenticated
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);
  ```
- **Migration shape:**
  ```sql
  -- supabase/migrations/20260527131455_add_profiles.sql
  CREATE TABLE IF NOT EXISTS public.profiles (
    user_id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 50),
    avatar_url text,
    updated_at timestamptz NOT NULL DEFAULT now()
  );
  ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
  ```
- **Edge function:**
  ```ts
  Deno.serve(async (req) => {
    const { name } = await req.json();
    return new Response(JSON.stringify({ ok: true, name }), {
      headers: { "content-type": "application/json" },
    });
  });
  ```

## Anti-patterns

- Creating a user-data table without `ENABLE ROW LEVEL SECURITY`.
- Enabling RLS but writing zero policies (silent total deny).
- Comparing client-passed `user_id` instead of `auth.uid()` inside a policy.
- Putting service-role keys in mobile / web client code.
- Editing an already-applied migration in place.
- `supabase.from("table").select()` without an RLS-aware filter where ownership matters.
- Unfiltered realtime subscriptions in client code.
