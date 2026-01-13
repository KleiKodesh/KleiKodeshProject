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

## Reading Background Integration
```css
--reading-bg-color: /* Custom reading background (light mode only) */
--reading-text-color: /* Auto-calculated contrasting text */
```

**CRITICAL**: Reading backgrounds only apply in light mode. Dark mode ignores these variables and uses default theme colors.

## Secondary Background Colors
Updated for better compatibility with reading backgrounds:
- **Light mode**: `#f8f8f8` (more neutral than previous `#f3f3f3`)
- **Dark mode**: `#2d2d2d` (more neutral than previous `#252526`)

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
5. **Reading background awareness** - Use theme-aware logic for reading components