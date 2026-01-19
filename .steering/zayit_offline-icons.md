---
inclusion: fileMatch
fileMatchPattern: '**/vue-zayit/**'
---

# Zayit Offline Icon System

## CRITICAL: Never Manually Add SVG Paths to iconify-offline.ts

**Problem**: @iconify/vue attempts to fetch icons from CDN at runtime (`api.iconify.design`, `api.simplesvg.com`, `api.unisvg.com`). This fails in VSTO WebView2 when offline with `ERR_INTERNET_DISCONNECTED`.

**Why VSTO Only**: The WebView2 virtual host correctly serves bundled files, but external CDN requests fail. The standalone wrapper may have different network access or caching.

## Solution: Use Automated Icon Extraction Script

### NEVER Do This (Manual SVG Addition)
```typescript
// ❌ WRONG - Never manually add SVG paths
const fluentIcons = {
  icons: {
    'new-icon': {
      body: '<path fill="currentColor" d="..."/>',  // DON'T DO THIS
      width: 24,
      height: 24
    }
  }
}
```

### ALWAYS Do This (Automated Script)

#### 1. Add Icon to Script List
Edit `scripts/extract-icons.js` and add the icon name to the `usedIcons` array:

```javascript
const usedIcons = [
  'spinner-ios-20-regular',
  'dismiss-16-regular',
  // ... existing icons
  'filter-28-regular',  // ✅ Add new icon here
  'your-new-icon-name'  // ✅ Add your icon
];
```

#### 2. Run Extraction Script
```bash
cd vue-zayit/zayit-vue
node scripts/extract-icons.js
```

#### 3. Verify Output
The script will:
- Extract SVG data from `@iconify-json/fluent`
- Generate `src/utils/iconify-offline.ts` automatically
- Show warnings for missing icons
- Report extraction count

### 4. Usage in Components
No changes needed - continue using Icon component normally:

```vue
<script setup>
import { Icon } from '@iconify/vue'
</script>

<template>
  <Icon icon="fluent:filter-28-regular" />
</template>
```

## Why This Process Matters

### Automated Benefits
- **Consistent format**: Script ensures proper SVG structure
- **Version control**: Icons come from official `@iconify-json/fluent` package
- **Error detection**: Script warns about missing icons
- **Maintainability**: Clear list of used icons in one place

### Manual Addition Problems
- **Outdated SVGs**: Manual paths may not match current icon versions
- **Format errors**: Incorrect width/height or malformed paths
- **No validation**: No way to verify icon exists or is correct
- **Maintenance burden**: Hard to track which icons are custom vs official

## Build Configuration

Vite config disables CDN API (optional but recommended):

```typescript
define: {
  'import.meta.env.VITE_ICONIFY_API': JSON.stringify(''),
}
```

This forces offline mode and prevents any CDN attempts.

## Initialize in main.ts
```typescript
import { initializeOfflineIcons } from './utils/iconify-offline'

// Initialize offline icons BEFORE creating app
initializeOfflineIcons()
```

## Troubleshooting

### Icon Not Found Warning
```
Warning: Icon "your-icon-name" not found in Fluent icon set
```

**Solution**: Check icon name at [Iconify Fluent Icons](https://icon-sets.iconify.design/fluent/) and ensure exact spelling.

### CDN Requests Still Happening
- Verify `initializeOfflineIcons()` is called before app creation
- Check Vite config has `VITE_ICONIFY_API` set to empty string
- Ensure icon name matches exactly what's in `usedIcons` array
