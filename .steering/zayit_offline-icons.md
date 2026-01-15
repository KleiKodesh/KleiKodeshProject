---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Offline Icon System

## CRITICAL: Iconify Requires Offline Configuration

**Problem**: @iconify/vue attempts to fetch icons from CDN at runtime (`api.iconify.design`, `api.simplesvg.com`, `api.unisvg.com`). This fails in VSTO WebView2 when offline with `ERR_INTERNET_DISCONNECTED`.

**Why VSTO Only**: The WebView2 virtual host correctly serves bundled files, but external CDN requests fail. The standalone wrapper may have different network access or caching.

## Solution: Preload Icon Collections

### 1. Icon Collection File
All icons used in the app are preloaded in `src/utils/iconify-offline.ts`:

```typescript
import { addCollection } from '@iconify/vue'

const fluentIcons = {
  prefix: 'fluent',
  icons: {
    'icon-name': {
      body: '<path fill="currentColor" d="..."/>',
      width: 24,
      height: 24
    },
    // ... more icons
  }
}

export function initializeOfflineIcons() {
  addCollection(fluentIcons)
}
```

### 2. Initialize in main.ts
```typescript
import { initializeOfflineIcons } from './utils/iconify-offline'

// Initialize offline icons BEFORE creating app
initializeOfflineIcons()
```

### 3. Usage in Components
No changes needed - continue using Icon component normally:

```vue
<script setup>
import { Icon } from '@iconify/vue'
</script>

<template>
  <Icon icon="fluent:spinner-ios-20-regular" />
</template>
```

## Adding New Icons

1. Find icon SVG path at [Iconify](https://icon-sets.iconify.design/fluent/)
2. Add to `fluentIcons.icons` object in `iconify-offline.ts`
3. Use in components with `icon="fluent:icon-name"`

## Build Configuration

Vite config disables CDN API (optional but recommended):

```typescript
define: {
  'import.meta.env.VITE_ICONIFY_API': JSON.stringify(''),
}
```

This forces offline mode and prevents any CDN attempts.
