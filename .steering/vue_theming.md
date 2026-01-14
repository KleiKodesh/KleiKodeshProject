---
inclusion: fileMatch
fileMatchPattern: '**/theme*|**/main.css|**/*.vue'
---

# Vue Theming System

## CSS Variables Structure
```css
:root {
  --bg-primary: #ffffff;
  --bg-secondary: #f8f8f8;
  --border-color: #e1e1e1;
  --text-primary: #1f1f1f;
  --text-secondary: #6f6f6f;
  --hover-bg: #f0f0f0;
  --accent-color: #0078d4;
}

:root.dark {
  --bg-primary: #1e1e1e;
  --bg-secondary: #2d2d2d;
  --border-color: #404040;
  --text-primary: #ffffff;
  --text-secondary: #cccccc;
  --hover-bg: #2a2a2a;
  --accent-color: #4fc3f7;
}
```

## Theme Toggle Implementation
```typescript
function toggleTheme() {
  isDarkTheme.value = !isDarkTheme.value
  
  if (isDarkTheme.value) {
    document.documentElement.classList.add('dark')
    localStorage.setItem('theme', 'dark')
  } else {
    document.documentElement.classList.remove('dark')
    localStorage.setItem('theme', 'light')
  }
}
```

## Reading Background Integration
```css
--reading-bg-color: '';
--reading-text-color: var(--text-primary);
```

**CRITICAL**: Reading backgrounds only apply in light mode. Dark mode ignores these variables.

## Component Rules
1. **Always use CSS variables** - Never hardcode colors
2. **Use semantic variables** - Prefer `--bg-primary` over specific colors
3. **Test both themes** - Ensure components work in light and dark
4. **Consistent hover states** - Use `--hover-bg` for all interactions

## Flat List Design Pattern
```css
.list-container {
    display: flex;
    flex-direction: column;
    /* NO gap property */
}

.list-item {
    padding: 4px 8px;
    /* NO margin, border, border-radius */
}

.list-item:hover {
    background: var(--hover-bg);
}
```

## Image Theming
For PNG/SVG files, use `themed-icon` class to auto-invert in dark mode:
```vue
<img src="icon.png" class="themed-icon" />
```