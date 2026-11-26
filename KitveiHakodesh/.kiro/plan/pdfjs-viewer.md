---
inclusion: fileMatch
fileMatchPattern: '**/pdf-reader/**|**/pdf-organizer/**|**/legacy-pdf/**|**/themes.ts|**/settingsStore.ts'
---

# PDF Viewer (PDF.js) Management

## Overview

The PDF viewer is `vue-frontend/src/features/pdf-reader/` (route `/pdf-view`), drawn in the page
itself - no iframe. It uses the `pdfjs-dist` npm package: the headless `PDFViewer`, `EventBus`,
`PDFLinkService` and `PDFFindController` from `pdfjs-dist/web/pdf_viewer.mjs`, with our own Vue
toolbar, search bar, side panel, editing bar, dialogs and shortcuts around it. The page organizer
window is `src/features/pdf-organizer/`.

The PDF.js source fork is the submodule `vue-frontend/pdf.js` (reference / future builds).
Never patch files in `node_modules/pdfjs-dist`; change behaviour in our Vue code.

## Theme

- `theme/themes.ts` `calcPdfFilter()` - the page colour filter for dark themes (a theme's own
  `pdfFilter`, else computed from the accent colour).
- `settingsStore.pdfPageFilters` (Settings, setup wizard) - turns the page filter on/off.
- The filter applies to the page canvases only (pages, side panel thumbnails, organizer
  thumbnails), never to our toolbars or the annotation-editor UI. PDF.js CSS variables
  (pdf_viewer.css) are mapped to the app's colour variables, so light/dark follow the app theme
  live in every window.

## Old viewer (archived)

The old iframe viewer - the patched PDF.js build that lived in `public/pdfjs/` and its host page
`PdfViewPage.vue` - is in `vue-frontend/legacy-pdf/` (see its README and `CUSTOMIZATIONS.md`).
It is not built or served; reference only.

## Upgrading PDF.js

1. Bump `pdfjs-dist` in `vue-frontend/package.json`, `npm install`.
2. Check the PDF.js changelog for `PDFViewer` / editor API changes used in `features/pdf-reader`
   and `features/pdf-organizer`, and for CSS variable renames in `pdf_viewer.css` (our theme
   mapping uses them).
3. Run the type check and test the viewer live (open, search, edit, save, print, organizer).
