# Injection — SQL, NoSQL, Command, XSS

## SQL injection
The classic. Any code that builds an SQL string from input is suspect.

**Safe (parameterized):**
```python
await db.execute("SELECT * FROM users WHERE email = $1", email)
await db.execute("UPDATE users SET name = $1 WHERE id = $2", name, uid)
```

**ORM (SQLAlchemy 2.x):**
```python
stmt = select(User).where(User.email == email)
await session.execute(stmt)
```

**Always wrong:**
```python
db.execute(f"SELECT * FROM users WHERE email = '{email}'")
db.execute("SELECT * FROM users WHERE email = '" + email + "'")
db.execute("SELECT * FROM users WHERE email = '%s'" % email)
```
Even with input "validation" — there's always an escape you missed.

**Dynamic identifiers (column / table names) — different problem.** Parameterization doesn't cover identifiers. Use an allowlist:
```python
ALLOWED_SORT_COLS = {"name", "created_at", "email"}
if sort_col not in ALLOWED_SORT_COLS: raise HTTPException(422)
stmt = text(f"SELECT * FROM users ORDER BY {sort_col}")
```

## NoSQL injection
MongoDB-style operator injection:
```js
// Attacker sends: {"email": {"$ne": null}}
User.findOne({ email: req.body.email })   // matches ANY user
```
**Fix:** validate types before query. Use strict schemas (Mongoose schemas, Pydantic).

## Command injection
```python
subprocess.run(f"convert {filename} out.png", shell=True)   # NEVER
subprocess.run(["convert", filename, "out.png"])             # safe; shell=False (default)
```
Never `shell=True` with input. Never `os.system(...)` with input.

## XSS (it's injection too, into the HTML/JS context)
- **Stored XSS**: attacker payload → DB → rendered into another user's page.
- **Reflected XSS**: payload in URL/form → echoed back in HTML response.
- **DOM XSS**: payload reaches `innerHTML`/`eval`/`document.write` client-side.

Defense:
- HTML-escape on output. React/JSX does this by default; never `dangerouslySetInnerHTML`.
- Use a strict CSP: `default-src 'self'; script-src 'self'` (no inline scripts, no `unsafe-eval`).
- For URLs in attributes: validate scheme (only `http(s)`, never `javascript:`).
