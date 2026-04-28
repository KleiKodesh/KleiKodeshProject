# PDF.js Viewer Management

## Overview

The PDF.js viewer is maintained in two locations and must be kept in sync:

- Source code: `Misc/pdf.js-master/` — the authoritative source for all viewer modifications
- Published build: `vue-frontend/public/pdfjs/` — the built viewer served by the Vue frontend

## Making Changes to the Viewer

All modifications must be made in `Misc/pdfjs-master/`, never directly in `vue-frontend/public/pdfjs/`. The `prebuild` npm script runs `Misc/scripts/publish_pdfjs.py` automatically before every `npm run build`, which copies the entire source folder to `vue-frontend/public/pdfjs/`. For dev work, run the script manually to sync changes without a full build.

## Directory Structure

- `Misc/pdfjs-master/web/viewer.html` — main viewer HTML; findbar lives here as a direct child of `#mainContainer`
- `Misc/pdfjs-master/web/viewer-custom.css` — all Zayit-specific CSS overrides
- `Misc/pdfjs-master/web/viewer.mjs` — PDF.js application bundle; patched for Hebrew locale, save dialog, and origin validation
- `Misc/pdfjs-master/Pdf.js_Cotumizations.md` — documents every customization made to the viewer
- `vue-frontend/public/pdfjs/` — published build; mirrors the above files after each update
- `vue-frontend/src/features/pdf-viewer/` — Vue integration layer that embeds the viewer in an iframe

## Key Rules

- Every customization must be documented in `Misc/pdfjs-master/Pdf.js_Cotumizations.md`
- Source and published files must always be in sync — a difference between them means an incomplete update
- The published build is what the app actually uses — always verify changes work in the Vue frontend before considering the task complete
