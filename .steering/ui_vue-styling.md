---
inclusion: fileMatch
fileMatchPattern: '**/*.vue|**/style*'
---

# Vue Component Styling

## Design System
- Base on Windows 11 Fluent Design + VS Code aesthetics
- Consistent spacing: 4px, 8px, 12px, 16px, 24px increments
- Typography: 'Segoe UI', system-ui, -apple-system, sans-serif

## Hebrew RTL Support
```css
.hebrew-content {
  direction: rtl;
  text-align: right;
  font-family: 'Segoe UI', 'Arial', sans-serif;
}
```

## Component Patterns

### Input Fields
```css
.input-field {
  border: 1px solid var(--border-color);
  background: var(--bg-primary);
  color: var(--text-primary);
  transition: border-color 0.15s ease;
}

.input-field:focus {
  border-color: var(--accent-color);
  box-shadow: 0 0 0 0.5px var(--accent-color);
}
```

### Buttons
```css
.btn-primary {
  background: var(--accent-color);
  border: 1px solid var(--accent-color);
  color: white;
  padding: 6px 12px;
  border-radius: 2px;
}
```

## SVG Icons
- Create as separate Vue components in `src/components/icons/`
- Naming: `Icon[Name].vue`
- Accept `size` (default 16px) and `color` (default currentColor) props
- Use viewBox="0 0 24 24" for Material Design icons