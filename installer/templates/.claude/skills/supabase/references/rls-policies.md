# RLS policies

## Enable RLS
```sql
ALTER TABLE public.profiles ENABLE ROW LEVEL SECURITY;
```
After this, ALL access from authenticated/anon roles is denied until policies exist.

## Owner-can-read
```sql
CREATE POLICY "owner reads own row"
  ON public.profiles
  FOR SELECT
  TO authenticated
  USING (auth.uid() = user_id);
```

## Owner-can-update
Both USING (which rows can be matched) and WITH CHECK (which rows can result):
```sql
CREATE POLICY "owner updates own row"
  ON public.profiles
  FOR UPDATE
  TO authenticated
  USING (auth.uid() = user_id)
  WITH CHECK (auth.uid() = user_id);
```

## Insert
```sql
CREATE POLICY "owner inserts own row"
  ON public.profiles
  FOR INSERT
  TO authenticated
  WITH CHECK (auth.uid() = user_id);
```

## Public read
```sql
CREATE POLICY "anyone reads published"
  ON public.posts
  FOR SELECT
  TO anon, authenticated
  USING (is_published = true);
```

## Service role
Bypasses RLS by design. Use ONLY from trusted backends / edge functions. Never expose `service_role` key to clients.

## Don't
- Don't `WHERE user_id = $1` from client and skip RLS. Always pair both.
- Don't write `USING (true)` — that's "allow all", almost always wrong.
- Don't forget WITH CHECK on UPDATE/INSERT. USING alone lets users mutate rows into someone else's ownership.
