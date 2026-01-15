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
- **NEVER run the app directly** - Use `npm run build` to check for issues or ask user to run the app
- Test with `npm run dev` immediately after setup

## Alt TOC Implementation Pattern
For displaying alternative TOC entries inline with content:

### Data Flow Architecture
```typescript
// 1. Parent loads TOC data and creates lookup map
const { tree, altTocByLineIndex } = buildTocFromFlat(tocEntriesFlat)

// 2. Pass tree to TOC component, map to line viewer
<BookTocTreeView :toc-entries="tree" />
<BookLineViewer :alt-toc-by-line-index="altTocByLineIndex" />

// 3. Line viewer passes entries to individual lines
<BookLine :alt-toc-entries="altTocByLineIndex?.get(lineIndex)" />
```

### Semantic HTML Headings
Use proper heading hierarchy with level offset:
```vue
<!-- Map TOC level to HTML heading with +1 offset -->
<component :is="getHeadingTag(entry.level)"
           class="alt-toc-entry"
           v-html="entry.text">
</component>

<script>
const getHeadingTag = (level: number): string => {
  // level 1 → h2, level 2 → h3, etc. (h1 reserved for main titles)
  const headingLevel = Math.max(2, Math.min(6, level + 2))
  return `h${headingLevel}`
}
</script>
```

### Styling Guidelines
```css
/* Use existing heading styles, add opacity for distinction */
.alt-toc-entry {
  opacity: 0.7; /* Subtle appearance */
}

/* Leverage existing heading CSS */
.book-line :deep(h2),
.book-line :deep(h3),
.book-line :deep(h4) {
  font-family: var(--header-font);
}
```