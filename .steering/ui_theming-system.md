---
inclusion: fileMatch
fileMatchPattern: '**/theme*|**/main.css'
---

# Theming System

## CSS Variables Structure
- **Primary theme file**: `src/assets/theme.css`
- **Import in**: `src/assets/main.css`
- **Application**: `:root` for light, `:root.dark` for dark

## Core Theme Variables
```css
--bg-primary: /* Main background */
--bg-secondary: /* Headers, sidebars */
--border-color: /* All borders */
--text-primary: /* Main text */
--text-secondary: /* Muted text */
--hover-bg: /* Hover states */
--accent-color: /* Primary accent */
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

## Component Rules
1. **Always use CSS variables** - Never hardcode colors
2. **Use semantic variables** - Prefer `--bg-primary` over specific colors
3. **Test both themes** - Ensure components work in light and dark
4. **Consistent hover states** - Use `--hover-bg` for all interactions