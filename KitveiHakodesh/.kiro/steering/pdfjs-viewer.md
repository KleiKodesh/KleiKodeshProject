# PDF.js Viewer Management

## Overview

The PDF.js viewer (v5.7.284) lives in `vue-frontend/public/pdfjs/` and is served via an iframe in `PdfViewPage.vue`. All customizations are applied directly to the files in that folder.

A copy of the original unmodified 5.5.207 dist is kept in `Misc/pdfjs-dist-5.5.207/` as a baseline for generating patch diffs. The full PDF.js source is in `Misc/pdf.js-master/` for reference.

The git branch `pdf-vue-toolbar` preserves a previous experiment adding a Vue toolbar wrapper. The current approach uses the native PDF.js toolbar inside the iframe.

## Customized Files

- `web/viewer.mjs` — 5 patches:
  1. Page cache size: `DEFAULT_CACHE_SIZE` reduced from 10 to 3 to cut canvas bitmap memory by ~70%
  2. Hebrew locale default: reads `?locale=` URL param, falls back to `he`
  3. Cross-origin allow: permits `http://` origins for WebView2 virtual hosts
  4. Filename param: reads `?filename=` and sets `_contentDispositionFilename`
  5. Save dialog: `_triggerDownload` uses `showSaveFilePicker()` with anchor fallback
- `web/viewer.html` — adds `viewer-custom.css` stylesheet and `pixel-ratio-override.js` script
- `web/viewer-custom.css` — CSS variable hooks for theme sync; PDF page filter via `data-pdf-filters` attribute
- `web/pixel-ratio-override.js` — forces minimum 2x `devicePixelRatio` for sharp rendering (added file)

## Vue Integration

- `PdfViewPage.vue` — embeds the viewer iframe; passes `?file=`, `?locale=he`, `?filename=`, `?cMapPacked=true`
- `themes.ts syncPdfViewerTheme()` — injects theme CSS variables and `--pdf-filter-custom` into the iframe
- `settingsStore.togglePdfPageFilters()` — sets `data-pdf-filters` attribute on iframe document element; `viewer-custom.css` applies the filter only when this attribute is `"true"`

## Upgrading PDF.js

1. Save the current dist: `Copy-Item -Recurse vue-frontend/public/pdfjs Misc/pdfjs-dist-X.X.XXX`
2. Download the new prebuilt release zip from GitHub releases
3. Replace `vue-frontend/public/pdfjs/` with the new build
4. Re-apply the 4 patches to `viewer.mjs` (search for the same surrounding code — it rarely moves far)
5. Re-add `viewer-custom.css` and `pixel-ratio-override.js`
6. Update `viewer.html` with the two extra tags
7. Update this file with the new version number

## Key Rules

- Never modify files in `Misc/pdfjs-dist-5.5.207/` — it is a read-only baseline
- Document any new customization here and in `vue-frontend/public/pdfjs/Pdf.js_Cotumizations.md`
