# CSS layout

## Mobile-first approach

Write base styles for small screens. Add complexity for larger screens with `min-width` media queries.

```css
/* Base — mobile */
.container {
  width: 100%;
  padding: 0 1rem;
}

/* Tablet+ */
@media (min-width: 768px) {
  .container {
    max-width: 720px;
    margin: 0 auto;
    padding: 0 1.5rem;
  }
}

/* Desktop+ */
@media (min-width: 1024px) {
  .container {
    max-width: 960px;
  }
}
```

Common breakpoints: `480px` (large phone), `768px` (tablet), `1024px` (desktop), `1280px` (wide).

---

## Flexbox

Use for 1-dimensional layout (row or column).

```css
/* Row — nav, toolbars, card rows */
.nav {
  display: flex;
  align-items: center;   /* vertical align */
  gap: 1rem;
}

/* Space between left and right */
.nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

/* Center an element */
.centered {
  display: flex;
  justify-content: center;
  align-items: center;
}

/* Column — stacked form, sidebar */
.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

/* Wrap cards */
.card-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}
.card {
  flex: 1 1 280px;   /* grow, shrink, basis — wraps when < 280px */
}
```

---

## Grid

Use for 2-dimensional layout (rows and columns).

```css
/* Fixed columns */
.grid-3 {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
}

/* Responsive — no media query needed */
.grid-auto {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 1rem;
}

/* Named areas — page layout */
.page {
  display: grid;
  grid-template-areas:
    "header header"
    "sidebar main"
    "footer footer";
  grid-template-columns: 240px 1fr;
  grid-template-rows: auto 1fr auto;
  min-height: 100vh;
}
.page-header  { grid-area: header; }
.page-sidebar { grid-area: sidebar; }
.page-main    { grid-area: main; }
.page-footer  { grid-area: footer; }
```

---

## Spacing system

Use a consistent scale to avoid arbitrary values:
```css
:root {
  --space-1: 0.25rem;   /* 4px */
  --space-2: 0.5rem;    /* 8px */
  --space-3: 0.75rem;   /* 12px */
  --space-4: 1rem;      /* 16px */
  --space-6: 1.5rem;    /* 24px */
  --space-8: 2rem;      /* 32px */
  --space-12: 3rem;     /* 48px */
}
```

---

## Box model

```css
/* Use border-box globally — width includes padding and border */
*, *::before, *::after {
  box-sizing: border-box;
}
```

---

## Common patterns

### Sticky header
```css
.header {
  position: sticky;
  top: 0;
  z-index: 100;
  background: white;
}
```

### Full-height page
```css
body {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}
main {
  flex: 1;  /* pushes footer to bottom */
}
```

### Centered card
```css
.card {
  max-width: 400px;
  width: 100%;
  margin: 2rem auto;
  padding: 2rem;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}
```
