# PDF.js Customizations

Base version: **5.7.284**
Applied to: `vue-frontend/public/pdfjs/`

When upgrading to a new PDF.js release, re-apply every item in this file.
Search for the surrounding code shown under each patch — line numbers shift between releases but the surrounding code is stable.

---

## Added Files

These files do not exist in the vanilla PDF.js dist and must be created fresh each upgrade.

### `web/pixel-ratio-override.js`

Forces a minimum `devicePixelRatio` of 1.5 for sharp PDF rendering on low-DPI displays.
Must be loaded **before** `viewer.mjs`.

Why 1.5× and not 2×: each canvas uses `width × height × devicePixelRatio²` bytes. At 2× every canvas is 4× larger than at 1×; at 1.5× canvases are 2.25× larger — still noticeably sharper than 1×, but using only 56% of the memory that 2× would require. On displays already at ≥1.5× (125%+ Windows scaling, retina) this script is a no-op.

```js
(function () {
  const original = window.devicePixelRatio || 1;
  const enhanced = Math.max(original, 1.5);
  if (enhanced !== original) {
    Object.defineProperty(window, 'devicePixelRatio', {
      get: function () { return enhanced; },
      configurable: true,
    });
  }
})();
```

### `web/viewer-custom.css`

Theme variable hooks and PDF page filter support. The Vue app's `syncPdfViewerTheme()` injects `--*-custom` CSS variables into the iframe; this file maps them onto PDF.js's own CSS variables so the viewer adopts the app's theme automatically.

The page filter (`--pdf-filter-custom`) is applied only when the `data-pdf-filters="true"` attribute is set on the iframe's `<html>` element, which `settingsStore.togglePdfPageFilters()` controls.

```css
:root {
  --toolbar-bg-color: var(--bg-primary-custom, light-dark(rgb(249 249 250), rgb(56 56 61)));
  --toolbar-border-color: var(--border-color-custom, light-dark(rgb(184 184 184), rgb(92 92 97)));
  --main-color: var(--text-primary-custom, light-dark(rgb(12 12 13), rgb(249 249 250)));
  --body-bg-color: var(--bg-secondary-custom, light-dark(rgb(212 212 215), rgb(35 35 39)));
  --progressBar-color: var(--accent-color-custom, #0a84ff);
  --doorhanger-bg-color: var(--bg-primary-custom, light-dark(rgb(255 255 255), rgb(56 56 61)));
  --doorhanger-border-color: var(--border-color-custom, light-dark(rgb(184 184 184), rgb(92 92 97)));
  --button-hover-color: var(--hover-bg-custom, light-dark(rgba(0 0 0 / 0.08), rgba(255 255 255 / 0.08)));
  --toggled-btn-bg-color: var(--active-bg-custom, light-dark(rgba(0 0 0 / 0.12), rgba(255 255 255 / 0.12)));
  --field-bg-color: var(--bg-primary-custom, light-dark(rgb(255 255 255), rgb(56 56 61)));
  --field-border-color: var(--border-color-custom, light-dark(rgb(187 187 188), rgb(115 115 115)));
  --separator-color: var(--border-color-custom, light-dark(rgba(0 0 0 / 0.08), rgba(255 255 255 / 0.08)));
}

:root[data-pdf-filters="true"] #viewerContainer .page canvas {
  filter: var(--pdf-filter-custom, none);
}

:root {
  --scrollbar-color: var(--border-color-custom, auto);
  --scrollbar-bg-color: transparent;
}
```

---

## `web/viewer.html` — Added Tags

Add these two lines immediately after `<link rel="stylesheet" href="viewer.css" />`:

```html
<link rel="stylesheet" href="viewer-custom.css" />
<script src="pixel-ratio-override.js"></script>
```

The `pixel-ratio-override.js` script tag must appear **before** `<script src="viewer.mjs" type="module">`.

---

## `web/viewer.mjs` — Patches

### 0. Canvas cleanup timeout (memory)

Search for: `const CLEANUP_TIMEOUT = 30000;`

Replace with:
```js
const CLEANUP_TIMEOUT = 5000; // Custom: reduced from 30000ms to 5000ms — frees canvas memory sooner when the user stops scrolling
```

PDF.js waits this many milliseconds of idle time before calling `cleanup()` on off-screen pages, which releases their canvas memory. 30 seconds is too long for a book reader where users frequently switch tabs or close PDFs. 5 seconds releases memory much sooner without any visible impact on scrolling performance.

---

### 0b. Memory-reduction AppOptions overrides

Search for: `function webViewerLoad() {`

Add the following block immediately after `const config = getViewerConfiguration();` and before the `const event = new CustomEvent(...)` line:

```js
// Custom: override AppOptions before the viewer initialises.
AppOptions.setAll({
  enableScripting: false,       // no embedded JS in Hebrew books
  enableDetailCanvas: false,    // no second high-res canvas overlay
  // annotationMode and annotationEditorMode left at defaults so the full
  // annotation editor (highlight, freetext, signature, etc.) is available
  enableAutoLinking: false,     // no URL scanning in text layer
  maxCanvasPixels: 4096 * 4096, // cap canvas at ~16M px, not 33M
});
```

These options cannot be set via URL params — they are only settable via `AppOptions.setAll()` from inside the viewer's JS context. Setting them here, before `PDFViewerApplication.run()`, ensures they take effect before any page is rendered.

---

### 1. Hebrew locale default

Search for: `lang: navigator.language || "en-US"`

Replace with:
```js
lang: new URLSearchParams(window.location.search).get("locale") || "he"
```

The Vue app passes `?locale=he` in the iframe src. This reads it and falls back to Hebrew if absent.

---

### 2. Cross-origin allow for WebView2 virtual hosts

Search for the `validateFileURL` function. Find this block:
```js
const fileOrigin = URL.parse(file, window.location)?.origin;
if (fileOrigin === viewerOrigin) {
  return;
}
```

Add immediately after:
```js
// Allow WebView2 virtual hosts (http:// origins for local file serving)
if (fileOrigin && fileOrigin.startsWith("http://")) {
  return;
}
```

Without this, PDF.js rejects files served from WebView2 virtual hostnames like `http://KitveiHakodesh-pdf-1/`.

---

### 3. Filename URL parameter

Search for: `validateFileURL(file);`

Add immediately after:
```js
// Custom: read filename param for document properties and save dialog
const customFilename = params.get("filename");
if (customFilename) {
  this._contentDispositionFilename = decodeURIComponent(customFilename);
}
```

The Vue app passes `?filename=encodedName` so the original filename appears in document properties and the save dialog.

---

### 4. Save dialog with File System Access API

Find the `DownloadManager` class and its `_triggerDownload` method. Replace the entire method with:

```js
_triggerDownload(blobUrl, originalUrl, filename, isAttachment = false) {
  // Custom: use File System Access API save dialog when available
  if (blobUrl && !isAttachment && window.showSaveFilePicker) {
    (async () => {
      try {
        const response = await fetch(blobUrl);
        const blob = await response.blob();
        const handle = await window.showSaveFilePicker({
          suggestedName: filename || "document.pdf",
          types: [{ description: "PDF Files", accept: { "application/pdf": [".pdf"] } }],
        });
        const writable = await handle.createWritable();
        await writable.write(blob);
        await writable.close();
        if (blobUrl.startsWith("blob:")) URL.revokeObjectURL(blobUrl);
        return;
      } catch {
        // User cancelled or API error — fall through to default anchor download
      }
    })();
    return;
  }
  this._defaultTriggerDownload(blobUrl, originalUrl, filename, isAttachment);
}
_defaultTriggerDownload(blobUrl, originalUrl, filename, isAttachment = false) {
  if (!blobUrl && !isAttachment) {
    if (!createValidAbsoluteUrl(originalUrl, "http://example.com")) {
      throw new Error(`_triggerDownload - not a valid URL: ${originalUrl}`);
    }
    blobUrl = originalUrl + "#pdfjs.action=download";
  }
  const a = document.createElement("a");
  a.href = blobUrl;
  a.target = "_parent";
  if ("download" in a) {
    a.download = filename;
  }
  (document.body || document.documentElement).append(a);
  a.click();
  a.remove();
}
```

Shows a native OS save dialog (Chrome/Edge/WebView2). Falls back to automatic download if the user cancels or the API is unavailable.

---

### 5. Feature flags — set to `true`

Search for each option name in `defaultOptions` and change `value: false` to `value: true`:

| Option | Why |
|---|---|
| `enableSplitMerge` | Page reorganization UI (select, copy, cut, delete, reorder pages) |
| `enableMerge` | PDF merge UI |
| `enableComment` | Comment/annotation sidebar |
| `enableHighlightFloatingButton` | Floating highlight button when text is selected |
| `enableSignatureEditor` | Signature editor tool |
| `enableUpdatedAddImage` | Updated image insertion UI |
| `enableNewBadge` | "NEW" badge on new features |
| `enableOptimizedPartialRendering` | Performance: optimized partial page rendering |

These three remain `false` intentionally:
- `enableAltText` — triggers a ~50MB AI model download, irrelevant for a book reader
- `enablePermissions` — would disable editing on publisher-protected PDFs
- `pdfBugEnabled` — developer debugging tool only

---

## Vue App Integration (no changes needed on upgrade)

These live in the Vue app and do not need to be re-applied to PDF.js:

- `PdfViewPage.vue` — passes `?file=`, `?locale=he`, `?filename=`, `?cMapPacked=true` to the iframe src
- `themes.ts syncPdfViewerTheme()` — injects `--*-custom` CSS variables and `--pdf-filter-custom` into the iframe
- `settingsStore.togglePdfPageFilters()` — sets `data-pdf-filters` attribute on the iframe document element
