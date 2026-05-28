# SKILL: react

## Purpose
Governs how the implementer writes React components, hooks, and data-fetching patterns. Scope: React 18+ (function components, hooks). Does NOT cover backend, routing beyond basics, or meta-frameworks like Next.js (those have their own conventions). Does NOT overlap with `html-css` — React projects follow component-based structure; raw HTML/CSS patterns still apply within JSX.

## Rules

1. **Function components only.** No class components. No `React.Component`.
2. **Hooks at the top level.** Never call hooks inside loops, conditions, or nested functions.
3. **One responsibility per component.** If a component fetches data AND renders a complex UI AND handles form state — split it.
4. **Lift state only as high as needed.** If only one component needs it, keep it local. Share via props or context, not globals.
5. **No direct DOM manipulation.** No `document.querySelector` inside components. Use refs (`useRef`) when DOM access is genuinely needed.
6. **Keys are stable and unique.** Never use array index as key for lists that can reorder or update. Use a data id.
7. **Effects have explicit deps.** Every `useEffect` has a dependency array. Empty `[]` means "run once on mount" — document why.
8. **Loading and error states are always rendered.** Every async operation has a loading state and an error state visible to the user.
9. **Forms are controlled.** Input values are driven by state (`value={state}` + `onChange`). No uncontrolled inputs except file inputs.
10. **Prop types are documented.** Use TypeScript types or JSDoc to declare expected props. No implicit `any`.

## Patterns

### Component structure
```jsx
// Named export — easier to find in a codebase
export function LoginForm({ onSuccess }) {
  const [email, setEmail] = useState('');
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await login(email);
      onSuccess();
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <label htmlFor="email">Email</label>
      <input
        id="email"
        type="email"
        value={email}
        onChange={e => setEmail(e.target.value)}
        required
      />
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={loading}>
        {loading ? 'Signing in…' : 'Sign in'}
      </button>
    </form>
  );
}
```

### Data fetching
```jsx
function UserList() {
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        const data = await fetchUsers();
        if (!cancelled) setUsers(data);
      } catch (err) {
        if (!cancelled) setError(err.message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => { cancelled = true; };  // cleanup — prevent setState on unmount
  }, []);  // empty deps: run once on mount

  if (loading) return <p>Loading…</p>;
  if (error)   return <p role="alert">Error: {error}</p>;
  return (
    <ul>
      {users.map(u => <li key={u.id}>{u.name}</li>)}
    </ul>
  );
}
```

### Custom hook
```jsx
function useUsers() {
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => { /* fetch logic */ }, []);

  return { users, loading, error };
}

// Component stays thin
function UserList() {
  const { users, loading, error } = useUsers();
  // ...
}
```

## Anti-patterns

- Class components — use function components.
- `useEffect` with no dependency array — runs after every render, almost always a bug.
- Array index as key: `key={index}` — use `key={item.id}`.
- Calling `setState` in a loop — batch updates, or use a reducer.
- Reading DOM with `document.querySelector` inside a component — use `useRef`.
- Fetching data without cleanup (memory leak on fast unmount).
- Showing nothing on loading/error — always render feedback.
