# React Hooks (React 18+)

## Rules of hooks — unchanged, but StrictMode changed

React 18 StrictMode double-invokes effects in dev (mount → unmount → remount). Effects MUST be idempotent and have correct cleanup.

```jsx
// Effects run twice in dev — cleanup MUST be correct
useEffect(() => {
  let active = true;
  fetchData().then(data => { if (active) setData(data); });
  return () => { active = false; }; // cleanup prevents stale setState
}, [id]);
```

## Root API changed in React 18

```jsx
// WRONG — React 17 API, opts out of concurrent features
import ReactDOM from 'react-dom';
ReactDOM.render(<App />, document.getElementById('root'));

// CORRECT — React 18+
import { createRoot } from 'react-dom/client';
const root = createRoot(document.getElementById('root'));
root.render(<App />);

// SSR hydration
import { hydrateRoot } from 'react-dom/client';
hydrateRoot(document.getElementById('root'), <App />);
```

## Automatic batching (React 18)

```jsx
// React 17: two renders. React 18: one render (automatic batching everywhere)
setTimeout(() => {
  setCount(c => c + 1);  // batched
  setFlag(f => !f);      // batched — single render
}, 1000);

// Opt out if needed
import { flushSync } from 'react-dom';
flushSync(() => setCount(c => c + 1)); // forces immediate render
```

## Concurrent features (React 18)

```jsx
import { useTransition, useDeferredValue, useId } from 'react';

// useTransition — mark update as non-urgent (can be interrupted)
const [isPending, startTransition] = useTransition();
startTransition(() => setSearchQuery(input));

// useDeferredValue — defer expensive re-renders
const deferredQuery = useDeferredValue(searchQuery);

// useId — stable IDs for SSR/hydration
const id = useId();
return <label htmlFor={id}>Name<input id={id} /></label>;
```

## React 19 hooks

```jsx
// use() — unwrap promises/context in render; causes Suspense
import { use } from 'react';
function Comments({ commentsPromise }) {
  const comments = use(commentsPromise); // suspends until resolved
  return comments.map(c => <p key={c.id}>{c.text}</p>);
}

// useActionState (React 19)
const [error, submitAction, isPending] = useActionState(
  async (prevState, formData) => {
    const err = await updateName(formData.get('name'));
    return err ?? null;
  },
  null
);

// useOptimistic (React 19)
const [optimisticName, setOptimisticName] = useOptimistic(currentName);

// ref as prop — no forwardRef needed in React 19
function MyInput({ placeholder, ref }) {
  return <input placeholder={placeholder} ref={ref} />;
}
```

## Common AI mistakes

| AI generates | Correct |
|---|---|
| `ReactDOM.render(...)` | `createRoot(...).render(...)` |
| `ReactDOM.hydrate(...)` | `hydrateRoot(...)` |
| `useEffect` without cleanup | Return cleanup function — always |
| Class components | Function components only |
| `useEffect` with no dependency array | Add `[]` and document why it's empty |
| `forwardRef` wrapper (React 19) | Pass `ref` directly as prop |
| Legacy Context (`childContextTypes`) — removed React 19 | `React.createContext()` |
