# Semantic HTML

## Why it matters for testing
Semantic elements expose meaning through the accessibility tree — which is exactly what Playwright reads via `browser_snapshot`. Using the right element means `getByRole('button')` and `getByLabel('Email')` work without extra attributes.

## Element reference

### Document structure
```html
<header>   — site/page header (logo, nav)
<nav>      — navigation links
<main>     — primary content (one per page)
<aside>    — sidebar, related content
<footer>   — page footer
<section>  — thematic grouping (use with a heading inside)
<article>  — self-contained content (blog post, card)
```

### Headings
One `<h1>` per page. Hierarchy must not skip levels (h1 → h2 → h3, never h1 → h3).
```html
<h1>Page title</h1>
<h2>Section</h2>
<h3>Subsection</h3>
```

### Interactive elements
```html
<button>          — triggers an action (submit, open modal, toggle)
<a href="/path">  — navigates to a URL
<input>           — user input (always paired with <label>)
<select>          — dropdown choice
<textarea>        — multiline input
```

Never use `<div>` or `<span>` for interactive elements.

### Forms
```html
<form>
  <!-- method + action for non-JS fallback -->
  <form method="POST" action="/login">

  <!-- Label pairing — explicit -->
  <label for="email">Email</label>
  <input type="email" id="email" name="email" required>

  <!-- Label pairing — implicit (wrapping) -->
  <label>
    Password
    <input type="password" name="password" required>
  </label>

  <!-- Submit -->
  <button type="submit">Sign in</button>
  <!-- Reset (rarely needed) -->
  <button type="reset">Clear</button>
</form>
```

### Input types
Use the right `type` — browsers show appropriate keyboards on mobile and validate format:
```html
<input type="email">      — validates email format
<input type="password">   — masks text
<input type="number">     — numeric keyboard, min/max attrs
<input type="tel">        — phone keyboard
<input type="url">        — URL keyboard
<input type="date">       — date picker
<input type="checkbox">   — boolean choice
<input type="radio">      — exclusive choice within a group
<input type="file">       — file upload
<input type="search">     — search box (adds clear button in some browsers)
```

### Lists
```html
<ul>  — unordered (order doesn't matter)
<ol>  — ordered (sequence matters: steps, rankings)
<dl>  — definition list (term + description pairs)
  <dt>Term</dt>
  <dd>Description</dd>
```

### ARIA when semantics fall short
Use sparingly — semantic HTML is always preferred:
```html
role="alert"            — live region, announced immediately by screen readers
role="status"           — polite live region (loading messages)
aria-label="Close"      — label when no visible text
aria-describedby="hint" — points to descriptive text element
aria-expanded="true"    — for toggle buttons (accordions, dropdowns)
aria-hidden="true"      — hides decorative elements from accessibility tree
```
