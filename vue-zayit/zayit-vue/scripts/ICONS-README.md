# Automatic Icon Extraction

This directory contains scripts for automatically extracting and bundling Iconify icons for offline use.

## How It Works

The app uses a fully automated icon extraction system that:

1. **Scans your codebase** for icon usage in `.vue`, `.ts`, `.tsx`, `.js`, and `.jsx` files
2. **Extracts only the icons you use** from the full Iconify packages
3. **Generates a bundled file** (`src/utils/iconify-offline.ts`) with icon data
4. **Runs automatically** during development and build

## Files

- `auto-extract-icons.js` - Main script that scans and extracts icons
- `vite-plugin-auto-icons.js` - Vite plugin that runs extraction automatically

## Usage

### Automatic (Recommended)

Icons are extracted automatically when you:

- Start the dev server: `npm run dev`
- Build for production: `npm run build`

The Vite plugin runs the extraction on startup, so you never need to manually run anything.

### Manual

If you want to manually regenerate the icon bundle:

```bash
npm run extract-icons
```

## Adding New Icons

Just use them in your code! The system will automatically detect and include them.

```vue
<template>
  <Icon icon="fluent:new-icon-24-regular" />
</template>
```

Next time you start the dev server or build, the new icon will be included.

## Icon Naming

Icons follow the Iconify naming convention:

- **Fluent icons**: `fluent:icon-name-size-variant`
  - Example: `fluent:book-open-24-regular`
- **Fluent Color icons**: `fluent-color:icon-name-size`
  - Example: `fluent-color:settings-24`

Browse available icons at: https://icon-sets.iconify.design/fluent/

## How Detection Works

The script uses regex to find icon usage patterns:

```typescript
// Detects these patterns:
icon = "fluent:book-24-regular";
icon = "fluent:search-28-filled";
icon = "fluent-color:settings-24";
```

## Offline Mode

The app is configured for complete offline operation:

- No CDN requests to Iconify API
- All icon data bundled in the app
- Works in WebView2 without internet
- Configured via `VITE_ICONIFY_API: ''` in vite configs

## Troubleshooting

### Icons not showing up

1. Check the icon name is correct (case-sensitive)
2. Run `npm run extract-icons` manually
3. Check console for warnings about missing icons
4. Verify the icon exists in the Iconify package

### Build fails

1. Make sure `glob` is installed: `npm install`
2. Check that `@iconify-json/fluent` and `@iconify-json/fluent-color` are in devDependencies

## Performance

- Only icons actually used in code are bundled
- Typical bundle size: ~50-100 icons = ~50KB
- No runtime API calls or network requests
- Icons load instantly from memory
