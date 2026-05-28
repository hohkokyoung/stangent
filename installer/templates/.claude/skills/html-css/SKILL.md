# SKILL: html-css

## Purpose
Governs how the implementer writes vanilla HTML, CSS, and JavaScript for web projects. Scope: structure, styling, and client-side behaviour without a framework. Does NOT cover backend, build tools, or framework-specific patterns (use the `react` skill for React projects).

## Rules

1. **Semantic HTML first.** Use the right element for the job — `<nav>`, `<main>`, `<section>`, `<article>`, `<button>`, `<form>`, `<label>`. Never use `<div>` for interactive elements.
2. **Every interactive element is keyboard-accessible.** Buttons are `<button>`, links that navigate are `<a href>`. Do not put click handlers on `<div>` or `<span>`.
3. **Every form input has a `<label>`.** Use `for`/`id` pairing or wrap input inside label. No placeholder-as-label.
4. **CSS: layout via Flexbox or Grid.** No floats for layout. No absolute positioning for flow content.
5. **Mobile-first CSS.** Base styles target small screens; use `min-width` media queries to scale up. Never use fixed pixel widths for containers.
6. **No inline styles for anything beyond one-off dynamic values.** Styles live in `.css` files or `<style>` blocks, not `style=""` attributes.
7. **JavaScript: use `const` and `let`, never `var`.** Use `addEventListener` not `onclick=""` attributes. Use `fetch` for HTTP — no XMLHttpRequest.
8. **DOM queries are cached.** Call `document.querySelector` once at the top of a module, not inside event handlers or loops.
9. **Error states are visible.** Every `fetch` call has a `.catch` (or try/catch with await). Errors are shown to the user, not only logged to console.

## Patterns

### HTML structure
```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Page Title</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <header>...</header>
  <main>
    <section aria-labelledby="section-heading">
      <h2 id="section-heading">Section</h2>
    </section>
  </main>
  <script src="app.js" defer></script>
</body>
</html>
```

### Form with label
```html
<form id="login-form">
  <label for="email">Email</label>
  <input type="email" id="email" name="email" required autocomplete="email">

  <label for="password">Password</label>
  <input type="password" id="password" name="password" required>

  <button type="submit">Sign in</button>
</form>
```

### Fetch with error handling
```javascript
async function submitForm(data) {
  try {
    const res = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!res.ok) throw new Error(`Server error: ${res.status}`);
    return await res.json();
  } catch (err) {
    showError(err.message);
  }
}
```

### CSS layout
```css
/* Mobile-first container */
.container {
  width: 100%;
  padding: 0 1rem;
}

@media (min-width: 768px) {
  .container {
    max-width: 720px;
    margin: 0 auto;
  }
}

/* Flexbox nav */
.nav {
  display: flex;
  align-items: center;
  gap: 1rem;
}
```

## Anti-patterns

- `<div onclick="...">` — use `<button>` or `<a>`.
- `placeholder="Email"` with no `<label>` — placeholders disappear on focus.
- `position: absolute` for layout — use Flexbox/Grid.
- `document.querySelector('#btn')` inside an event handler — query once, reuse.
- Swallowing fetch errors with empty `.catch(() => {})`.
- `var` — use `const`/`let`.
- Fixed widths like `width: 1200px` on containers.
