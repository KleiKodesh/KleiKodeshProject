---
inclusion: manual
---

# Iconify Offline Icons System

## Overview

The app uses a fully automated offline icon system that extracts and bundles only the icons used in the codebase. No CDN requests, no manual maintenance, completely automatic.

## How It Works

### Automatic Extraction Process

1. **Scan Phase** - Before dev/build, scans all `.vue`, `.ts`, `.tsx`, `.js`, `.jsx` files
2. **Detection** - Finds icon usage via regex: `icon="fluent:icon-name"` or `icon="fluent-color:icon-name"`
3. **Extraction** - Extracts only used icons from `@iconify-json/fluent` and `@iconify-json/fluent-color` packages
4. **Generation** - Creates `src/utils/iconify-offline.ts` with bundled icon data
5. **Bundling** - Vite includes the minimal icon file in the app bundle

### Why Not Tree-Shakable?

**The Problem:**

- Iconify packages contain 1000+ icons each
- Icons are referenced by string names at runtime: `icon="fluent:book-24-regular"`
- JavaScript bundlers can't analyze string values to determine which icons are used
- Traditional tree-shaking requires static imports, not dynamic string references

**Our Solution:**

- Build-time static analysis of source code
- Scans for icon string patterns before bundling
- Extracts only detected icons
- Better than tree-shaking: works with any icon reference pattern

## Usage

### Adding Icons

Just use them in your code - they're automatically detected and bundled:

```vue
<template>
  <Icon icon="fluent:book-open-24-regular" />
  <Icon icon="fluent:search-28-filled" />
  <Icon icon="fluent-color:settings-24" />
</template>

<script setup lang="ts">
  import { Icon } from "@iconify/vue";
</script>
```

### Icon Naming Convention

- **Fluent icons**: `fluent:icon-name-size-variant`
  - Example: `fluent:book-open-24-regular`
  - Variants: `regular`, `filled`
  - Sizes: `16`, `20`, `24`, `28`, `48`

- **Fluent Color icons**: `fluent-color:icon-name-size`
  - Example: `fluent-color:settings-24`
  - Pre-colored icons with gradients

Browse available icons: https://icon-sets.iconify.design/fluent/

### Automatic Bundling

Icons are extracted automatically when you:

```bash
npm run dev      # Extracts icons, starts dev server
npm run build    # Extracts icons, builds for production
```

Manual extraction (rarely needed):

```bash
npm run extract-icons
```

## Implementation Details

### Files

- `scripts/auto-extract-icons.js` - Main extraction script
- `scripts/vite-plugin-auto-icons.js` - Vite plugin that runs extraction
- `src/utils/iconify-offline.ts` - Generated icon bundle (auto-generated, don't edit)
- `scripts/ICONS-README.md` - Detailed documentation

### Vite Configuration

Both `vite.config.ts` and `vite.config.prod.ts` include:

```typescript
import { autoIconsPlugin } from "./scripts/vite-plugin-auto-icons.js";

export default defineConfig({
  plugins: [
    autoIconsPlugin(), // Runs extraction on startup
    // ... other plugins
  ],
  define: {
    // Disable Iconify API - force offline mode
    "import.meta.env.VITE_ICONIFY_API": JSON.stringify(""),
  },
});
```

### Detection Pattern

The script uses regex to find icon usage:

```javascript
const iconPattern = /icon=["']([^"']+)["']/g;
```

Matches:

- `icon="fluent:book-24-regular"`
- `icon='fluent:search-28-filled'`
- `icon="fluent-color:settings-24"`

### Generated Output

Example `iconify-offline.ts`:

```typescript
import { addCollection } from "@iconify/vue";

const fluentIcons = {
  prefix: "fluent",
  icons: {
    "book-open-24-regular": { body: "...", width: 24, height: 24 },
    "search-28-filled": { body: "...", width: 28, height: 28 },
    // ... only icons found in codebase
  },
  width: 24,
  height: 24,
};

const fluentColorIcons = {
  prefix: "fluent-color",
  icons: {
    "settings-24": { body: "...", width: 24, height: 24 },
    // ... only color icons found in codebase
  },
  width: 24,
  height: 24,
};

export function initializeOfflineIcons() {
  addCollection(fluentIcons);
  addCollection(fluentColorIcons);
}
```

### Initialization

Icons are loaded in `src/main.ts` before app mount:

```typescript
import { initializeOfflineIcons } from "@/utils/iconify-offline";

// Initialize offline icons for WebView2 environment
initializeOfflineIcons();

// ... rest of app initialization
```

## Benefits

1. **Zero Maintenance** - No manual icon lists to maintain
2. **Minimal Bundle Size** - Only icons you use (typically 50-100 icons = ~50KB)
3. **Fully Offline** - No CDN requests, works in WebView2 without internet
4. **Automatic Updates** - Add/remove icons in code, they're automatically detected
5. **Type Safety** - TypeScript knows about all available icons
6. **Fast Loading** - Icons load instantly from memory, no network delay

## Troubleshooting

### Icons Not Showing

1. Check icon name is correct (case-sensitive)
2. Verify icon exists: https://icon-sets.iconify.design/fluent/
3. Run `npm run extract-icons` manually
4. Check console for warnings about missing icons
5. Restart dev server

### Build Fails

1. Ensure `glob` is installed: `npm install`
2. Check `@iconify-json/fluent` and `@iconify-json/fluent-color` are in devDependencies
3. Verify Node.js version matches `package.json` engines requirement

### Icon Not Detected

The script only detects this pattern:

```vue
<!-- ✅ Detected -->
<Icon icon="fluent:book-24-regular" />

<!-- ❌ Not detected (dynamic) -->
<Icon :icon="dynamicIconName" />
```

For dynamic icons, add them to a comment to ensure detection:

```vue
<!-- Icons used dynamically: 
  icon="fluent:book-24-regular"
  icon="fluent:search-28-filled"
-->
```

## Performance

- **Scan time**: ~100-200ms for typical codebase
- **Extraction time**: ~50ms
- **Bundle size**: ~1KB per icon (SVG path data)
- **Runtime overhead**: Zero (icons loaded once at startup)
- **Network requests**: Zero (fully offline)

## Best Practices

1. **Use consistent naming** - Stick to Fluent icon set for consistency
2. **Prefer regular variant** - Use `regular` over `filled` unless you need emphasis
3. **Match icon size to usage** - Use 20px for small UI, 24px for standard, 28px for prominent
4. **Don't over-iconify** - Use icons purposefully, not decoratively
5. **Test offline** - Verify icons work without internet connection

## Migration from CDN

If migrating from CDN-based Iconify:

1. Remove any `<script>` tags loading Iconify from CDN
2. Ensure `@iconify/vue` is installed: `npm install @iconify/vue`
3. Import `Icon` component: `import { Icon } from '@iconify/vue'`
4. Use icons normally - extraction is automatic
5. Build and verify icons work offline

## Future Enhancements

Possible improvements:

- Watch mode: Re-extract when icon usage changes during dev
- Icon usage report: Show which icons are used where
- Unused icon detection: Warn about icons in bundle but not in code
- Custom icon sets: Support for non-Fluent icon packages
- Dynamic icon support: Better handling of runtime icon names
