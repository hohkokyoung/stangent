# JavaScript DOM & fetch

## Module pattern

Structure JS as modules. Use `defer` on the script tag so the DOM is ready.

```html
<script src="app.js" defer></script>
```

```javascript
// app.js — query elements once at top level
const form = document.querySelector('#login-form');
const emailInput = document.querySelector('#email');
const errorMsg = document.querySelector('#error-message');

// attach listeners
form.addEventListener('submit', handleSubmit);

async function handleSubmit(event) {
  event.preventDefault();
  // ...
}
```

---

## DOM queries

```javascript
// Single element
document.querySelector('#id')           // by id
document.querySelector('.class')        // by class (first match)
document.querySelector('[data-id="x"]') // by attribute

// Multiple elements
document.querySelectorAll('.card')      // returns NodeList
[...document.querySelectorAll('.item')] // spread to array for map/filter

// From a parent (scoped query)
const list = document.querySelector('#results');
list.querySelectorAll('li')
```

---

## DOM manipulation

```javascript
// Text + HTML
el.textContent = 'Safe text';          // safe — no XSS
el.innerHTML = '<b>Bold</b>';          // only with trusted content

// Attributes
el.setAttribute('aria-expanded', 'true');
el.getAttribute('data-id');
el.removeAttribute('disabled');

// Classes
el.classList.add('active');
el.classList.remove('active');
el.classList.toggle('open');
el.classList.contains('loading');      // → boolean

// Visibility
el.hidden = true;                      // sets hidden attribute
el.style.display = 'none';            // inline style (use sparingly)

// Creating elements
const li = document.createElement('li');
li.textContent = 'New item';
list.appendChild(li);

// Removing elements
el.remove();
```

---

## Events

```javascript
// Preferred: addEventListener
button.addEventListener('click', handleClick);
input.addEventListener('input', handleInput);   // fires on every keystroke
form.addEventListener('submit', handleSubmit);
window.addEventListener('load', init);

// Remove when no longer needed
button.removeEventListener('click', handleClick);

// Event delegation — one listener for many children
list.addEventListener('click', (event) => {
  const item = event.target.closest('li');
  if (!item) return;
  // handle item click
});
```

---

## Fetch

```javascript
// GET
async function getData(url) {
  try {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } catch (err) {
    showError(err.message);
    return null;
  }
}

// POST with JSON body
async function postData(url, body) {
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
    showError(err.message);
    return null;
  }
}
```

---

## Loading and error states

Always reflect async state in the UI:

```javascript
function setLoading(isLoading) {
  submitBtn.disabled = isLoading;
  submitBtn.textContent = isLoading ? 'Loading...' : 'Submit';
}

function showError(message) {
  errorMsg.textContent = message;
  errorMsg.hidden = false;
}

function clearError() {
  errorMsg.textContent = '';
  errorMsg.hidden = true;
}

// Usage
async function handleSubmit(e) {
  e.preventDefault();
  clearError();
  setLoading(true);
  const result = await postData('/api/login', { email: emailInput.value });
  setLoading(false);
  if (result) {
    window.location.href = '/dashboard';
  }
}
```

---

## Local storage

```javascript
// Save
localStorage.setItem('token', value);

// Read
const token = localStorage.getItem('token'); // null if missing

// Remove
localStorage.removeItem('token');
```

Only store non-sensitive data. Never store passwords or full user objects.
