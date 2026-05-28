# React data fetching

## Pattern: fetch in useEffect

Use for simple cases without a fetching library.

```jsx
function UserProfile({ userId }) {
  const [user, setUser]       = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/users/${userId}`)
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return res.json();
      })
      .then(data => { if (!cancelled) setUser(data); })
      .catch(err  => { if (!cancelled) setError(err.message); })
      .finally(()  => { if (!cancelled) setLoading(false); });

    return () => { cancelled = true; };  // prevent setState on unmount
  }, [userId]);  // re-fetch when userId changes

  if (loading) return <p aria-live="polite">Loading…</p>;
  if (error)   return <p role="alert">Error: {error}</p>;
  if (!user)   return null;
  return <div>{user.name}</div>;
}
```

---

## Custom fetch hook

Extract fetch logic to keep components thin:

```jsx
function useFetch(url) {
  const [data, setData]       = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState(null);

  useEffect(() => {
    if (!url) return;
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(url)
      .then(res => { if (!res.ok) throw new Error(`HTTP ${res.status}`); return res.json(); })
      .then(d  => { if (!cancelled) setData(d); })
      .catch(e => { if (!cancelled) setError(e.message); })
      .finally(() => { if (!cancelled) setLoading(false); });

    return () => { cancelled = true; };
  }, [url]);

  return { data, loading, error };
}

// Usage — component has no fetch logic
function UserProfile({ userId }) {
  const { data: user, loading, error } = useFetch(`/api/users/${userId}`);
  if (loading) return <p>Loading…</p>;
  if (error)   return <p role="alert">{error}</p>;
  return <div>{user?.name}</div>;
}
```

---

## Mutations (POST/PUT/DELETE)

Mutations are triggered by user action, not on mount:

```jsx
function useSubmit(url) {
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState(null);

  async function submit(body) {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        throw new Error(data.message ?? `HTTP ${res.status}`);
      }
      return await res.json();
    } catch (err) {
      setError(err.message);
      return null;
    } finally {
      setLoading(false);
    }
  }

  return { submit, loading, error };
}

// Usage
function LoginForm({ onSuccess }) {
  const { submit, loading, error } = useSubmit('/api/login');

  async function handleSubmit(e) {
    e.preventDefault();
    const result = await submit({ email, password });
    if (result) onSuccess(result);
  }
  // ...
}
```

---

## Abort on unmount

Always abort fetch calls when the component unmounts to prevent state updates on dead components:

```jsx
useEffect(() => {
  const controller = new AbortController();

  fetch(url, { signal: controller.signal })
    .then(...)
    .catch(err => {
      if (err.name === 'AbortError') return;  // ignore intentional abort
      setError(err.message);
    });

  return () => controller.abort();
}, [url]);
```

---

## Loading and error UI requirements

Every async operation must render three states:

| State | What to show |
|---|---|
| Loading | Spinner, skeleton, or "Loading…" text with `aria-live="polite"` |
| Error | Error message with `role="alert"` so screen readers announce it |
| Empty | "No results" message — don't render an empty list silently |
| Data | The actual content |

```jsx
if (loading) return <p aria-live="polite">Loading users…</p>;
if (error)   return <p role="alert">Failed to load: {error}</p>;
if (!data?.length) return <p>No users found.</p>;
return <ul>{data.map(u => <li key={u.id}>{u.name}</li>)}</ul>;
```
