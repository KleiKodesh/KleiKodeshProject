# PDF.js Modifications Documentation

This document tracks all modifications made to PDF.js and its viewer for the Zayit Vue project.

## Overview

PDF.js is used in this project to display PDF files within the Vue application. Several customizations have been made to integrate it properly with the Vue app's theming system and to force Hebrew localization.

## File Locations

- **PDF.js Directory**: `vue-zayit/zayit-vue/public/pdfjs/`
- **Viewer Files**: `vue-zayit/zayit-vue/public/pdfjs/web/`
- **Locale Files**: `vue-zayit/zayit-vue/public/pdfjs/web/locale/`

## Modifications Made

### 1. CORS/Origin Validation Bypass (viewer.mjs)

**File**: `vue-zayit/zayit-vue/public/pdfjs/web/viewer.mjs`

**Location**: Line ~3481-3486

**Original Code**:
```javascript
const fileOrigin = new URL(file, window.location.href).origin;

if (fileOrigin !== viewerOrigin) {
  throw new Error("file origin does not match viewer's");
}
```

**Modified Code**:
```javascript
const fileOrigin = new URL(file, window.location.href).origin;

// Allow virtual host origins for C# PDF manager (*.app.local)
if (fileOrigin.endsWith('.app.local')) {
  return;
}

if (fileOrigin !== viewerOrigin) {
  throw new Error("file origin does not match viewer's");
}
```

**Purpose**: Allows PDF.js to load PDFs from virtual host origins (e.g., `https://zayitHost.app.local`) used by the C# WebView2 PDF manager.

**Context**: 
- C# uses `SetVirtualHostNameToFolderMapping` to create virtual HTTPS hosts
- These virtual hosts have different origins than the viewer
- Without this modification, PDF.js blocks loading from these origins
- Used for Hebrew Books cache: `https://zayitHost/pdfjs/web/hebrewbookscache/`

**Revert Instructions**: Remove the 4 lines that check for `.app.local` origins.

### 2. Hebrew Locale Force (viewer.mjs)

**File**: `vue-zayit/zayit-vue/public/pdfjs/web/viewer.mjs`

**Location**: Line ~4545-4548

**Original Code**:
```javascript
defaultOptions.locale = {
  value: navigator.language || "en-US",
  kind: OptionKind.VIEWER
};
```

**Modified Code**:
```javascript
defaultOptions.locale = {
  value: "he",
  kind: OptionKind.VIEWER
};
```

**Purpose**: Forces PDF.js to always use Hebrew locale for tooltips and UI text, regardless of browser language settings.

**Effect**: All PDF.js tooltips now display in Hebrew:
- דף קודם (Previous page)
- דף הבא (Next page)
- התקרבות (Zoom in)
- התרחקות (Zoom out)
- הדפסה (Print)
- שמירה (Save)
- פתיחה (Open)

### 2. Hebrew Locale Force (viewer.mjs)

**File**: `vue-zayit/zayit-vue/public/pdfjs/web/viewer.mjs`

**Location**: Line ~4545-4548

**Original Code**:
```javascript
defaultOptions.locale = {
  value: navigator.language || "en-US",
  kind: OptionKind.VIEWER
};
```

**Modified Code**:
```javascript
defaultOptions.locale = {
  value: "he",
  kind: OptionKind.VIEWER
};
```

**Purpose**: Forces PDF.js to always use Hebrew locale for tooltips and UI text, regardless of browser language settings.

**Effect**: All PDF.js tooltips now display in Hebrew:
- דף קודם (Previous page)
- דף הבא (Next page)
- התקרבות (Zoom in)
- התרחקות (Zoom out)
- הדפסה (Print)
- שמירה (Save)
- פתיחה (Open)

**Revert Instructions**: Change `value: "he"` back to `value: navigator.language || "en-US"`

### 3. Vue Integration Changes

**Files Modified**:
- `vue-zayit/zayit-vue/src/components/pages/PdfViewPage.vue`
- `vue-zayit/zayit-vue/src/components/pages/HebrewBooksViewPage.vue`
- `vue-zayit/zayit-vue/src/utils/theme.ts`

#### A. PDF Viewer URL with Hebrew Locale Parameter

**PdfViewPage.vue** - Lines ~69-85:

**Original**:
```javascript
const pdfViewerUrl = computed(() => {
  const baseUrl = '/pdfjs/web/viewer.html';
  const fileSource = selectedPdfUrl.value;
  const finalUrl = fileSource
    ? `${baseUrl}?file=${encodeURIComponent(fileSource)}`
    : baseUrl;
  return finalUrl;
});
```

**Modified**:
```javascript
const pdfViewerUrl = computed(() => {
  const baseUrl = '/pdfjs/web/viewer.html';
  const fileSource = selectedPdfUrl.value;
  
  // Build URL with file parameter and Hebrew locale
  const params = new URLSearchParams();
  if (fileSource) {
    params.set('file', fileSource);
  }
  // Force Hebrew locale for tooltips
  params.set('locale', 'he');

  const finalUrl = `${baseUrl}?${params.toString()}`;
  return finalUrl;
});
```

**HebrewBooksViewPage.vue** - Similar modification for Hebrew books PDF viewer.

#### B. Theme Synchronization System

**theme.ts** - Enhanced with PDF.js theme sync:

**Added Functions**:
- `syncPdfViewerTheme()` - Syncs Vue app theme with PDF.js viewer
- `setupPdfViewerThemeObserver()` - Automatically syncs new PDF iframes
- Enhanced `toggleTheme()` and `initTheme()` to sync PDF viewers

**Purpose**: Ensures PDF.js viewer theme (dark/light) matches the Vue app theme.

**How it works**:
- Detects Vue app theme state (dark/light)
- Finds PDF.js iframes in the DOM
- Applies `.is-dark` or `.is-light` classes to PDF.js HTML element
- Uses MutationObserver to handle dynamically loaded PDF viewers

#### C. PDF Component Theme Integration

**PdfViewPage.vue** and **HebrewBooksViewPage.vue**:

**Added**:
- `@load="onPdfIframeLoad"` event handlers
- `syncPdfViewerTheme()` calls when PDF iframes load
- Import of theme utilities

**Purpose**: Ensures PDF viewers get correct theme when they load.

## Theme System Integration

### PDF.js Theme Classes Used

PDF.js uses these CSS classes for theming:
- `.is-dark` - Forces dark mode
- `.is-light` - Forces light mode
- No class - Uses system preference (`prefers-color-scheme`)

### Vue App Theme Sync

The Vue app automatically:
1. Detects when theme is toggled
2. Finds all PDF.js iframes
3. Applies appropriate theme class to iframe document
4. Handles new PDF viewers automatically

## Locale System

### Available Locales

PDF.js includes many locales in `vue-zayit/zayit-vue/public/pdfjs/web/locale/`:
- `he/` - Hebrew (עברית)
- `en-US/` - English (US)
- `ar/` - Arabic (العربية)
- And many others...

### Hebrew Locale Files

- **Main file**: `vue-zayit/zayit-vue/public/pdfjs/web/locale/he/viewer.ftl`
- **Contains**: Hebrew translations for all PDF.js UI elements
- **Format**: Fluent localization format (.ftl)

### Locale Configuration

PDF.js locale is configured in two ways:
1. **Default locale** (viewer.mjs modification) - Always uses Hebrew
2. **URL parameter** (Vue components) - Backup method using `?locale=he`

## Reverting Changes

### To Revert CORS/Origin Bypass:

1. **Edit** `vue-zayit/zayit-vue/public/pdfjs/web/viewer.mjs`
2. **Find** lines ~3483-3486:
   ```javascript
   // Allow virtual host origins for C# PDF manager (*.app.local)
   if (fileOrigin.endsWith('.app.local')) {
     return;
   }
   ```
3. **Remove** these 4 lines
4. **Result**: PDF.js will only load PDFs from same origin
5. **Impact**: Hebrew Books and C# virtual host PDFs will not load

### To Revert Hebrew Locale Force:

1. **Edit** `vue-zayit/zayit-vue/public/pdfjs/web/viewer.mjs`
2. **Find** line ~4545: `value: "he",`
3. **Change to**: `value: navigator.language || "en-US",`
4. **Clear browser cache** and refresh

### To Revert Theme Integration:

1. **Remove theme sync calls** from PDF component load handlers
2. **Revert theme.ts** to original version without PDF sync functions
3. **Remove locale parameters** from PDF viewer URLs in Vue components

### To Use System Locale:

1. **Revert viewer.mjs** locale change (see above)
2. **Remove** `params.set('locale', 'he');` from Vue components
3. PDF.js will use browser's language setting

## Testing Changes

### Verify Hebrew Locale:
1. Open PDF in Vue app
2. Hover over PDF.js toolbar buttons
3. Tooltips should display in Hebrew

### Verify Theme Sync:
1. Toggle Vue app theme (light/dark)
2. PDF.js viewer should immediately switch themes
3. Open new PDF - should use current app theme

### Browser Cache:
- **Clear cache** after modifying viewer.mjs
- **Hard refresh** (Ctrl+F5) may be needed
- **Restart dev server** if running in development

## Notes

- **PDF.js Version**: Check `vue-zayit/zayit-vue/public/pdfjs/build/pdf.mjs` for version info
- **Backup**: Keep original viewer.mjs backup before modifications
- **Updates**: When updating PDF.js, reapply these modifications
- **Cross-Origin**: Theme sync only works when PDF.js is served from same origin

## Future Considerations

- **PDF.js Updates**: Will overwrite modifications - document and reapply
- **Additional Locales**: Can be added by modifying locale parameter logic
- **Theme Customization**: PDF.js CSS can be further customized in viewer.css
- **Performance**: MutationObserver has minimal performance impact but can be optimized if needed