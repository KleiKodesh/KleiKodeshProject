# PDF.js Theme Synchronization

This PDF.js installation is configured to sync with the Zayit Vue app's theme instead of using the system theme preference.

## How It Works

### PDF.js Native Theme System

PDF.js has a built-in theme system controlled by the `viewerCssTheme` option:

- `0` = Auto (follows system preference)
- `1` = Light theme
- `2` = Dark theme

### Vue App Integration

The Vue app's theme utility (`zayit-vue/src/utils/theme.ts`) automatically syncs themes by:

1. **Detecting PDF.js iframes** - Finds all iframes with `/pdfjs/web/viewer.html` in their src
2. **Accessing PDF.js API** - Uses `window.PDFViewerApplicationOptions` exposed by PDF.js
3. **Setting theme directly** - Calls `AppOptions.set('viewerCssTheme', themeValue)`
4. **Applying color-scheme** - Sets CSS `color-scheme` property like PDF.js does internally

### Implementation Details

```javascript
// In Vue app theme.ts
const iframeWindow = iframe.contentWindow;
const AppOptions = iframeWindow.PDFViewerApplicationOptions;

// Set PDF.js theme: 1 = light, 2 = dark
const themeValue = isDark ? 2 : 1;
AppOptions.set("viewerCssTheme", themeValue);

// Set color-scheme property
const docStyle = iframeWindow.document.documentElement.style;
docStyle.setProperty("color-scheme", isDark ? "dark" : "light");
```

## Automatic Sync

Theme synchronization happens automatically:

- **On app initialization** - `initTheme()` syncs existing PDF viewers
- **On theme toggle** - `toggleTheme()` immediately syncs all PDF viewers
- **On iframe load** - PDF viewer components call `syncPdfViewerTheme()` when loaded
- **On dynamic content** - MutationObserver detects new PDF iframes and syncs them

## Benefits

- **Native PDF.js theming** - Uses PDF.js's own theme system, not CSS overrides
- **Reliable synchronization** - Works with PDF.js's internal theme logic
- **No CSS conflicts** - Doesn't interfere with PDF.js's existing styles
- **Automatic detection** - Finds and syncs all PDF viewers without manual configuration

## Files Modified

- **viewer.html** - No modifications needed (removed CSS override link)
- **theme.ts** - Enhanced with PDF.js native theme API calls
- **Vue components** - Already call `syncPdfViewerTheme()` on iframe load

## Debugging

Use browser console to debug theme sync:

```javascript
// Check current theme
window.zayitTheme.current();

// Force sync all PDF viewers
window.zayitTheme.sync();

// Toggle theme
window.zayitTheme.toggle();
```

The theme sync function logs detailed information about each iframe processed and whether the theme was successfully applied.
