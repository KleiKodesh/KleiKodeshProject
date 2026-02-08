# PDF.js Customizations

This PDF.js installation includes several customizations for optimal performance, visual quality, and Hebrew localization in the Zayit Vue app.

## Hebrew Localization

### Problem

PDF.js by default uses the browser's language setting (`navigator.language`), which may not be Hebrew. The viewer needs to be hardcoded to Hebrew locale for consistent RTL layout and Hebrew UI text.

### Solution

Modified PDF.js's internal locale detection to read from URL parameter with Hebrew as default.

### Implementation

**File**: `public/pdfjs/web/viewer.mjs` (line ~652)

Modified the `localeProperties` configuration:

```javascript
localeProperties: {
  value: {
    lang: new URLSearchParams(window.location.search).get("locale") || "he"
  },
  kind: OptionKind.BROWSER
},
```

**Original code:**

```javascript
localeProperties: {
  value: {
    lang: navigator.language || "en-US"
  },
  kind: OptionKind.BROWSER
},
```

### How It Works

1. PDF.js reads the `locale` URL parameter (e.g., `viewer.html?locale=he`)
2. Falls back to `"he"` if no parameter is provided
3. Loads Hebrew translations from `locale/he/viewer.ftl`
4. Automatically sets `dir="rtl"` because Hebrew is in PDF.js's RTL languages list: `["ar", "he", "fa", "ps", "ur"]`
5. Applies Hebrew locale to all UI elements (tooltips, buttons, menus)

### Vue App Integration

Both PDF viewer components pass the locale parameter:

**Files:**

- `src/components/pages/PdfViewPage.vue`
- `src/components/pages/HebrewBooksViewPage.vue`

```javascript
params.set("locale", "he");
```

### Benefits

- **Consistent Hebrew UI** - All tooltips and buttons display in Hebrew
- **Automatic RTL layout** - PDF.js detects Hebrew and applies right-to-left layout
- **Proper page spreading** - Pages spread right-to-left for Hebrew books
- **URL parameter support** - Can override locale if needed (e.g., `?locale=en-US`)
- **Uses PDF.js internal methods** - No external scripts or hacks required
- **Complete sidebar translations** - Added missing "views-manager" translations for the newer sidebar interface

### Missing Translations Fixed

The original Hebrew locale file (`locale/he/viewer.ftl`) was missing translations for the newer "views-manager" sidebar interface introduced in recent PDF.js versions. We added the following translations:

- Sidebar toggle button labels
- View selector (Pages, Outline, Attachments, Layers)
- Page management actions (Copy, Cut, Delete, Save as)
- Status messages for undo operations
- Warning messages for failed operations

**Translation style:** Used declarative "חלונית צד" (side panel) instead of action-oriented phrases for better Hebrew UX.

### References

- [PDF.js Issue #11829](https://github.com/mozilla/pdf.js/issues/11829) - Setting locale doesn't work
- [StackOverflow Solution](https://stackoverflow.com/questions/64915575/how-to-force-set-locale-in-pdf-js) - How to force locale in PDF.js

## Theme Synchronization

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

### Automatic Sync

Theme synchronization happens automatically:

- **On app initialization** - `initTheme()` syncs existing PDF viewers
- **On theme toggle** - `toggleTheme()` immediately syncs all PDF viewers
- **On iframe load** - PDF viewer components call `syncPdfViewerTheme()` when loaded
- **On dynamic content** - MutationObserver detects new PDF iframes and syncs them

## Sharpness Enhancement

### Problem

PDF.js renders at low resolution on displays with `devicePixelRatio` of 1.0, causing blurry text and graphics, especially when zoomed out on small viewports.

### Solution

Override `window.devicePixelRatio` to force high-DPI rendering before PDF.js initializes.

### Implementation

**File**: `public/pdfjs/web/pixel-ratio-override.js`

```javascript
// Force higher pixel ratio for sharper rendering
const originalDevicePixelRatio = window.devicePixelRatio || 1;
const enhancedPixelRatio = Math.max(originalDevicePixelRatio, 2); // Minimum 2x

// Override devicePixelRatio property
Object.defineProperty(window, "devicePixelRatio", {
  get: function () {
    return enhancedPixelRatio;
  },
  configurable: true,
});
```

**Integration**: Added to `viewer.html` before PDF.js loads:

```html
<!-- Override devicePixelRatio for sharper rendering - MUST load first -->
<script src="pixel-ratio-override.js"></script>
```

### How It Works

1. PDF.js `OutputScale` class uses `globalThis.devicePixelRatio || 1`
2. Canvas dimensions calculated as `width * outputScale.sx` where `outputScale.sx = pixelRatio`
3. By forcing `devicePixelRatio` ≥ 2, all PDF canvases render at 2x+ resolution
4. Provides same sharp rendering as Edge's built-in viewer

### Benefits

- **Sharp text and graphics** at all zoom levels
- **Automatic scaling** - PDF.js handles high-DPI rendering internally
- **No performance impact** on high-DPI displays (already using high pixel ratio)
- **Universal improvement** - Works for all PDF content

## Performance Optimization

### Problem

Large PDF files load slowly, with pages taking time to appear when scrolling.

### Solution

Enable PDF.js built-in performance optimizations via URL parameters.

### Implementation

**Files**:

- `src/components/pages/PdfViewPage.vue` (regular PDF viewer)
- `src/components/pages/HebrewBooksViewPage.vue` (HebrewBooks PDF viewer)

```javascript
// Performance optimizations for large files
params.set("disableAutoFetch", "false"); // Enable auto-fetch for better performance
params.set("disableStream", "false"); // Enable streaming for faster loading
params.set("disableRange", "false"); // Enable range requests for partial loading
params.set("enableHWA", "true"); // Enable hardware acceleration
params.set("cMapPacked", "true"); // Use packed CMaps for faster font loading
```

### Optimizations Explained

1. **Range Requests** (`disableRange=false`)
   - Downloads only needed parts of PDF
   - Faster initial loading, loads visible pages first
   - Critical for large files

2. **Streaming** (`disableStream=false`)
   - Progressive loading of PDF data
   - PDF starts rendering before fully downloaded
   - Reduces perceived loading time

3. **Auto-Fetch** (`disableAutoFetch=false`)
   - Automatically fetches pages in background
   - Pages load faster when user scrolls
   - Improves navigation experience

4. **Hardware Acceleration** (`enableHWA=true`)
   - Uses GPU for rendering when available
   - Faster page rendering and smoother scrolling
   - Better performance on complex pages

5. **Packed CMaps** (`cMapPacked=true`)
   - Compressed font character mappings
   - Faster font loading, especially for non-Latin text
   - Reduces font-related delays

### Benefits

- **Faster initial loading** - Pages appear progressively
- **Smoother scrolling** - Background loading and GPU acceleration
- **Better large file handling** - Only loads what's needed
- **Improved font performance** - Especially important for Hebrew text

## Files Modified

### Hebrew Localization

- **viewer.mjs** (line ~652) - Modified `localeProperties` to read from URL parameter with Hebrew default
- **locale/he/viewer.ftl** - Added missing "views-manager" translations for sidebar interface
- **PdfViewPage.vue** - Passes `locale=he` parameter
- **HebrewBooksViewPage.vue** - Passes `locale=he` parameter

### Theme System

- **viewer.html** - No modifications needed (removed CSS override link)
- **theme.ts** - Enhanced with PDF.js native theme API calls
- **Vue components** - Already call `syncPdfViewerTheme()` on iframe load

### Sharpness Enhancement

- **viewer.html** - Added `pixel-ratio-override.js` script
- **pixel-ratio-override.js** - New file with devicePixelRatio override

### Performance Optimization

- **PdfViewPage.vue** - Added performance URL parameters
- **HebrewBooksViewPage.vue** - Added performance URL parameters

## Debugging

### Theme Sync

Use browser console to debug theme sync:

```javascript
// Check current theme
window.zayitTheme.current();

// Force sync all PDF viewers
window.zayitTheme.sync();

// Toggle theme
window.zayitTheme.toggle();
```

### Sharpness Enhancement

Check console for pixel ratio override confirmation:

```
[PDF.js Enhancement] Original devicePixelRatio: 1, Enhanced: 2
```

### Performance Optimization

Check browser Network tab to verify:

- Range requests are being used (partial content responses)
- Streaming is active (progressive loading)
- Only visible pages are initially loaded

## References

- **Sharpness Solution**: [StackOverflow - PDF.js Low Resolution Fix](https://stackoverflow.com/questions/49426385/how-to-fix-pdf-documents-from-being-rendered-in-really-low-resolution-blurry)
- **PDF.js Options**: Built-in `AppOptions` system in PDF.js viewer
- **Performance Settings**: PDF.js URL parameter documentation
