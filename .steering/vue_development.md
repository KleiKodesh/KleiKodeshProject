---
inclusion: fileMatch
fileMatchPattern: '**/*.vue|**/vite.config.*|**/package.json'
---

# Vue Development Standards

## Single File Output (Required for VSTO)
Configure Vite for single bundled file to avoid CORS issues in WebView2:

```typescript
// vite.config.ts
export default defineConfig({
  build: {
    rollupOptions: {
      output: {
        manualChunks: undefined,
        inlineDynamicImports: true,
        entryFileNames: 'index.js',
        chunkFileNames: 'index.js',
        assetFileNames: '[name].[ext]'
      }
    },
    assetsInlineLimit: 100000000, // Inline all assets
    cssCodeSplit: false
  }
})
```

## Component Styling Standards

### Design System
- Windows 11 Fluent Design + VS Code aesthetics
- Spacing: 4px, 8px, 12px, 16px, 24px increments
- Typography: 'Segoe UI', system-ui, -apple-system, sans-serif

### Button Pattern
```css
.btn-primary {
  background: var(--accent-color);
  border: 1px solid var(--accent-color);
  color: white;
  padding: 6px 12px;
  border-radius: 2px;
  user-select: none; /* REQUIRED: Prevent text selection */
}
```

### Hebrew RTL Support
```css
.hebrew-content {
  direction: rtl;
  text-align: right;
  font-family: 'Segoe UI', 'Arial', sans-serif;
}
```

## Icons - @iconify/vue Approach
```vue
<script setup>
import { Icon } from '@iconify/vue'
</script>

<template>
  <Icon icon="fluent:home-28-regular" />
  <Icon icon="fluent:search-28-filled" />
</template>
```

## Function-Based Architecture
**Avoid ES6 classes** - Use functions for better flexibility:

```javascript
// ✅ GOOD - Function-based approach
function createComponent() {
    const state = { /* ... */ };
    function initialize() { /* ... */ }
    return { initialize, show, hide };
}

// ❌ AVOID - ES6 classes
class Component { /* ... */ }
```

## Development Setup
- Always run `npm install` immediately after project creation
- Use TypeScript, Router, Pinia, Vitest, ESLint, Prettier as standard
- Remove template content - create clean projects
- Test with `npm run dev` immediately after setup