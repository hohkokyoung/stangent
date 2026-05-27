# Access control — authentication + authorization + IDOR

OWASP's #1 category in 2021. The most common bug: checking the user is logged in, but not checking they own the resource.

## Authentication vs Authorization

- **Authentication (authn)**: who are you? (login)
- **Authorization (authz)**: what can you do? (per-request, per-resource)

The most dangerous bug is doing authn but not authz.

## IDOR — Insecure Direct Object Reference

```python
@router.get("/orders/{order_id}")
async def get_order(order_id: int, user: User = Depends(get_current_user)):
    return await db.fetch_one("SELECT * FROM orders WHERE id = $1", order_id)
    # AUTHENTICATED but not AUTHORIZED — any logged-in user sees any order
```

**Fix:** filter by owner on every query.
```python
return await db.fetch_one(
    "SELECT * FROM orders WHERE id = $1 AND user_id = $2",
    order_id, user.id,
)
```

**Better:** push authz to the DB layer with RLS (Supabase / Postgres):
```sql
CREATE POLICY "owner reads own order"
  ON public.orders FOR SELECT
  TO authenticated
  USING (auth.uid() = user_id);
```
RLS makes the safe path the only path — even a buggy query can't bypass it.

## Trusting client-supplied user_id

```python
# Attacker's request body: {"user_id": "<someone else's id>", "name": "Hacked"}
@router.post("/profile")
async def update_profile(payload: ProfileUpdate, user: User = Depends(get_current_user)):
    await db.execute("UPDATE profiles SET name = $1 WHERE user_id = $2",
                     payload.name, payload.user_id)   # WRONG — uses client value
```

**Fix:** ignore client-supplied identity. Use the authenticated user.
```python
await db.execute("UPDATE profiles SET name = $1 WHERE user_id = $2",
                 payload.name, user.id)   # uses authn principal
```

## Authentication essentials

- **Passwords**: Argon2id (memory ≥ 64 MiB, time ≥ 3) or bcrypt cost ≥ 12. Never MD5/SHA1/SHA-256-unsalted.
- **Session tokens**: `secrets.token_urlsafe(32)` minimum. HttpOnly + Secure + SameSite cookies.
- **Rate-limit login**: per-IP and per-account, exponential backoff. Lockout policy with admin reset path.
- **No "user exists?" oracle**: same response for unknown email vs wrong password (and same timing — `hmac.compare_digest`).
- **Logout invalidates server-side session** (not just clears the cookie).
- **MFA**: TOTP minimum. Send-an-SMS isn't MFA, it's a downgrade vector.

## JWTs — the footguns

- **`alg: none` accepted**: don't. Whitelist the algorithm server-side.
- **`alg` confusion**: HS256 vs RS256 swap. Pin algorithm.
- **Long expiry**: tokens that don't expire are passwords with extra steps. 15min access + refresh token.
- **No revocation**: short-lived access tokens + revocable refresh tokens.
- **Secrets in JWT payload**: anyone with the token can read it. Payload is signed, not encrypted.

## Roles vs attributes

- **RBAC** (role-based): `admin`, `staff`, `user`. Coarse, but easy to reason about.
- **ABAC** (attribute-based): "user can edit X if X.owner == user.id AND X.status == 'draft'". Expressive but easy to get wrong.

Whichever you pick, **enforce server-side, every request**. Client-side role checks are UX, not security.
