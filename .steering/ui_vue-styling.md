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
  user-select: none; /* REQUIRED: Prevent text selection on buttons */
}

.btn-secondary {
  background: none;
  border: 1px solid var(--border-color);
  color: var(--text-primary);
  padding: 6px 12px;
  border-radius: 2px;
  user-select: none; /* REQUIRED: Prevent text selection on buttons */
}
```

**CRITICAL**: All buttons MUST include `user-select: none` to prevent text selection.

## Icons - @iconify/vue Approach

### Primary Method: Use @iconify/vue
```vue
<script setup>
import { Icon } from '@iconify/vue'
</script>

<template>
  <Icon icon="fluent:home-28-regular" />
  <Icon icon="fluent:search-28-filled" />
  <Icon icon="fluent:settings-28-regular" />
</template>
```

### Fallback: Custom Components
**Only create custom icon components when @iconify doesn't have the specific icon needed**

Custom icon pattern (when needed):
```vue
<template>
  <svg class="icon-name"
       width="20"
       height="20"
       viewBox="0 0 20 20"
       fill="none">
    <path d="..." 
          stroke="currentColor"
          stroke-width="1.5"
          fill="none" />
  </svg>
</template>

<style scoped>
.icon-name {
  flex-shrink: 0;
}
</style>
```

### Icon Usage Priority
1. **First**: Check @iconify for Fluent UI icons (`fluent:icon-name-size-variant`)
2. **Second**: Check @iconify for Material Design icons (`mdi:icon-name`)
3. **Last Resort**: Create custom SVG component

## Dropdown Positioning (RTL Layout)

### Viewport Boundary Detection
```javascript
// Check if dropdown would overflow viewport
const rect = dropdownElement.getBoundingClientRect()
const viewportHeight = window.innerHeight
const spaceBelow = viewportHeight - rect.bottom
const spaceAbove = rect.top

// Position dropdown to stay within viewport
if (spaceBelow < dropdownHeight && spaceAbove > spaceBelow) {
  // Show above trigger
  dropdown.style.bottom = '100%'
  dropdown.style.top = 'auto'
} else {
  // Show below trigger (default)
  dropdown.style.top = '100%'
  dropdown.style.bottom = 'auto'
}
```

### RTL Dropdown Positioning
```css
.dropdown-menu {
  position: absolute;
  top: 100%;
  right: 0; /* For RTL, anchor to right */
  left: auto;
  max-height: 300px; /* Prevent viewport overflow */
  overflow-y: auto;
}

/* Ensure dropdown arrow/trigger has proper spacing in RTL */
.dropdown-toggle {
  margin-right: 8px; /* Space from right edge in RTL */
  margin-left: auto;
}
```

### Common RTL Dropdown Issues
- **Arrow too close to edge**: Add `margin-right` to dropdown trigger
- **Dropdown escapes viewport**: Use `max-height` and `overflow-y: auto`
- **Wrong anchor point**: Use `right: 0` instead of `left: 0` for RTL