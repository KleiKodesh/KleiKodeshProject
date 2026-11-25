# legacy-pdf (archived, unused)

The old PDF viewer: the patched PDF.js build that ran in an iframe, and the Vue page and
helpers that hosted it. Replaced by the new viewer in `src/features/pdf-reader/`.

- `src-features-pdf-viewer/` - was `src/features/pdf-viewer/` (PdfViewPage and its helpers).
- `public-pdfjs/` - was `public/pdfjs/` (viewer.html, viewer.mjs, build/, CUSTOMIZATIONS.md, ...).
- `src-utils/outlineSearchPortEquivalence.test.ts` - was the test of `public/pdfjs/web/outline-search.js`.

Kept for reference only. Nothing in the app imports it, and it is neither built nor served
(outside `src/` and `public/`, so tsconfig, vitest and the Vite build never see it).
Import paths inside these files still point at their old locations and do not resolve.
