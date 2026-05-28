# React hooks

## useState

```jsx
const [value, setValue] = useState(initialValue);

// Functional update — when new state depends on old state
setValue(prev => prev + 1);

// Object state — always spread, never mutate
const [form, setForm] = useState({ email: '', password: '' });
setForm(prev => ({ ...prev, email: 'new@example.com' }));
```

---

## useEffect

```jsx
// Run once on mount
useEffect(() => {
  fetchData();
}, []);

// Run when dep changes
useEffect(() => {
  document.title = `${count} items`;
}, [count]);

// Cleanup (subscriptions, timers, abort)
useEffect(() => {
  const controller = new AbortController();
  fetch(url, { signal: controller.signal })
    .then(r => r.json())
    .then(setData)
    .catch(err => { if (err.name !== 'AbortError') setError(err.message); });
  return () => controller.abort();
}, [url]);
```

**Lint rule:** every value used inside the effect that comes from outside must be in the deps array, or it's a bug.

---

## useRef

```jsx
// DOM access
const inputRef = useRef(null);
<input ref={inputRef} />
// Later:
inputRef.current.focus();

// Mutable value that doesn't trigger re-render
const timerRef = useRef(null);
timerRef.current = setTimeout(...);
clearTimeout(timerRef.current);
```

---

## useCallback

Memoises a function — prevents child re-renders when the function identity changes:

```jsx
const handleClick = useCallback(() => {
  doSomething(id);
}, [id]);   // recreated only when id changes
```

Only add `useCallback` when you can measure a real perf problem. Don't add it everywhere.

---

## useMemo

Memoises an expensive computed value:

```jsx
const sortedItems = useMemo(() => {
  return [...items].sort((a, b) => a.name.localeCompare(b.name));
}, [items]);
```

Same rule: measure before adding. useMemo has overhead too.

---

## useContext

```jsx
// 1. Create
const ThemeContext = createContext('light');

// 2. Provide (high up in tree)
<ThemeContext.Provider value="dark">
  <App />
</ThemeContext.Provider>

// 3. Consume (anywhere in tree)
function Button() {
  const theme = useContext(ThemeContext);
  return <button className={theme}>Click</button>;
}
```

Use context for values that many components need (auth, theme, locale). Don't use it as a replacement for all prop passing.

---

## useReducer

For complex state with multiple sub-values or actions:

```jsx
const initialState = { count: 0, step: 1 };

function reducer(state, action) {
  switch (action.type) {
    case 'increment': return { ...state, count: state.count + state.step };
    case 'set_step':  return { ...state, step: action.payload };
    default: throw new Error(`Unknown action: ${action.type}`);
  }
}

function Counter() {
  const [state, dispatch] = useReducer(reducer, initialState);
  return (
    <>
      <p>{state.count}</p>
      <button onClick={() => dispatch({ type: 'increment' })}>+</button>
    </>
  );
}
```

---

## Custom hooks — naming and shape

```jsx
// Always starts with "use"
function useLocalStorage(key, initialValue) {
  const [value, setValue] = useState(() => {
    try {
      return JSON.parse(localStorage.getItem(key)) ?? initialValue;
    } catch {
      return initialValue;
    }
  });

  function setItem(newValue) {
    setValue(newValue);
    localStorage.setItem(key, JSON.stringify(newValue));
  }

  return [value, setItem];
}

// Usage
const [token, setToken] = useLocalStorage('auth_token', null);
```
