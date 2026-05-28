# React component patterns

## Component file structure

```
src/
  components/       — reusable, dumb (UI only)
    Button.jsx
    Modal.jsx
  features/         — feature-scoped components + hooks
    auth/
      LoginForm.jsx
      useAuth.js
  pages/            — route-level components (thin, compose features)
    LoginPage.jsx
    Dashboard.jsx
```

One component per file. File name matches component name.

---

## Props

```jsx
// Explicit prop types with destructuring + defaults
function Card({ title, description, variant = 'default', onClick }) {
  return (
    <div className={`card card--${variant}`} onClick={onClick}>
      <h3>{title}</h3>
      <p>{description}</p>
    </div>
  );
}

// Spreading remaining props (useful for wrapping native elements)
function Input({ label, id, error, ...inputProps }) {
  return (
    <div>
      <label htmlFor={id}>{label}</label>
      <input id={id} aria-describedby={error ? `${id}-error` : undefined} {...inputProps} />
      {error && <span id={`${id}-error`} role="alert">{error}</span>}
    </div>
  );
}
```

---

## Composition over configuration

Prefer slots (children/render props) over ever-growing prop lists:

```jsx
// Bad — too many props for layout variants
<Card title="X" subtitle="Y" icon={<Icon />} footer={<Btn />} />

// Good — compose freely
<Card>
  <Card.Header>
    <Icon /> X
  </Card.Header>
  <Card.Body>Y</Card.Body>
  <Card.Footer><Btn /></Card.Footer>
</Card>

// Implementation with compound components
function Card({ children }) {
  return <div className="card">{children}</div>;
}
Card.Header = ({ children }) => <div className="card__header">{children}</div>;
Card.Body   = ({ children }) => <div className="card__body">{children}</div>;
Card.Footer = ({ children }) => <div className="card__footer">{children}</div>;
```

---

## Conditional rendering

```jsx
// Short-circuit — only when condition is boolean, not 0/null
{isLoggedIn && <Dashboard />}

// Ternary — when you have an else branch
{loading ? <Spinner /> : <Content />}

// Early return — for complex guards
function UserProfile({ user }) {
  if (!user) return <p>Not found</p>;
  if (user.banned) return <p>Account suspended</p>;
  return <div>{user.name}</div>;
}
```

---

## List rendering

```jsx
// Key must be stable and unique — use data id, not index
{items.map(item => (
  <ListItem key={item.id} item={item} />
))}

// Empty state
{items.length === 0
  ? <p>No items found.</p>
  : items.map(item => <ListItem key={item.id} item={item} />)
}
```

---

## Lifting state

When two siblings need the same state, lift it to their closest common parent:

```jsx
function Parent() {
  const [selected, setSelected] = useState(null);
  return (
    <>
      <List items={items} onSelect={setSelected} />
      <Detail item={selected} />
    </>
  );
}
```

Rule: only lift as high as needed. Don't put everything in a root context.

---

## Error boundaries

For catching render errors in a subtree (use a library like `react-error-boundary`):

```jsx
import { ErrorBoundary } from 'react-error-boundary';

<ErrorBoundary fallback={<p>Something went wrong.</p>}>
  <RiskyComponent />
</ErrorBoundary>
```

Hooks can't catch render errors — you need a class-based error boundary or the library wrapper.

---

## Performance rules

1. Split large components — smaller components re-render less.
2. Colocate state — state at the right level prevents unnecessary re-renders.
3. `memo` only after measuring — `React.memo(Component)` skips re-render if props are shallowly equal, but adds comparison overhead.
4. Avoid creating objects/arrays inline in JSX — they create new references each render, breaking memoisation.
```jsx
// Bad — new object every render
<Component style={{ margin: 0 }} />

// Good
const style = { margin: 0 };  // outside component
<Component style={style} />
```
