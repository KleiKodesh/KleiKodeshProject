# PDF Viewer

PDF viewing with OCR-based text extraction and script recognition.

## Files

**Page & UI:**
- `PdfViewPage.vue` — main PDF viewer page; manages iframe lifecycle, toolbar visibility, and OCR mode toggle; propagates the app-wide hidden-scrollbars setting into the viewer iframe via `useIframeScrollbarsHidden`
- `PdfOcrResultPopup.vue` — modal popup showing extracted/recognized text with copy functionality
- `usePdfPrintShortcut.ts` — composable; forwards Ctrl+P into the PDF.js iframe when the parent document has focus. PDF.js only handles Ctrl+P itself while focus is inside the iframe; outside it, the app-wide handler in `useAppTitleBarShortcuts` swallows the key (preventDefault only) to block the browser print dialog, and this listener calls `contentWindow.print()` so the PDF prints either way. In split view only the focused pane's instance responds, and Ctrl+Alt+P is excluded (PDF.js presentation mode)

**OCR Logic:**
- `usePdfOcrSelection.ts` — composable; manages Tesseract workers, iframe injection, and OCR workflow coordination
- `pdfOcrInjectedScript.ts` — script injected into the PDF.js iframe; handles rectangle selection, text layer extraction, and canvas capture
- `pdfViewerTypes.ts` — TypeScript types: `OcrScript` (`'hebrew' | 'rashi' | 'mixed' | 'english'`), `OcrSelectionResult`

**Page Tracking:**
- `usePdfViewPageTracking.ts` — composable; reads the PDF's built-in outline (TOC) via `PDFViewerApplication.pdfDocument.getOutline()` and resolves each entry's destination to a 1-based page number. On every `pagechanging` event it finds the deepest outline entry whose page ≤ the current page and writes the full ancestor breadcrumb (e.g. "פרק א · סימן ב") as `tocPath` on the active tab. Falls back to "עמוד X מתוך Y" when the PDF has no outline. `AppTitleBar` renders `tocPath` the same way it renders the book view TOC breadcrumb. Call `attach(contentWindow)` after iframe load and `detach()` before tear-down.

**Store:**
- `pdfOcrStore.ts` (in `src/stores/`) — OCR UI state: active flag, script selection, skip-existing-text flag

## OCR Workflow

### Activation

1. User clicks the OCR button in the title bar (only visible on `/pdf-view` tabs)
2. `pdfOcrStore.toggle()` sets `isActive = true`
3. `PdfViewPage.vue` watches the store and calls `ocr.activate()`
4. `usePdfOcrSelection.activate()` injects `pdfOcrInjectedScript` into the PDF.js iframe
5. The injected script switches the cursor to a crosshair and waits for a drag selection

### Selection & Text Extraction

1. User drags a rectangle over the PDF content
2. The injected script's `processRect()` function runs:
   - **First:** attempts `extractText(rect)` — queries all `.textLayer span` elements inside the selection
   - **If text found:** posts `kitvei-hakodesh-ocr-result` message with `isOcr: false` — no Tesseract involved
   - **If no text:** calls `captureCanvas(rect)` to extract the PDF rendering as a PNG data URL, then posts `kitvei-hakodesh-ocr-canvas` with the data URL

### OCR Processing (if needed)

1. `usePdfOcrSelection` receives the `kitvei-hakodesh-ocr-canvas` message
2. Sets `isProcessing = true` and shows the popup immediately (with empty text and progress bar)
3. Initializes a Tesseract worker for the selected script (`hebrew`, `rashi`, `mixed`, or `english`)
4. Calls `worker.recognize(dataUrl)` to OCR the canvas image
5. Cleans up the recognized text and posts the result to the popup
6. After popup closes, OCR mode deactivates automatically

### Result Display & Copy

The `PdfOcrResultPopup.vue` modal shows:
- Badge indicating source: "טקסט נבחר" (text layer) or "טקסט מזוהה (OCR)" (recognized)
- Editable textarea with the extracted/recognized text
- Progress bar (during OCR processing)
- Copy button — copies to clipboard via `navigator.clipboard.writeText()` with fallback to `document.execCommand('copy')`
- Cancel button — dismisses the popup and deactivates OCR mode

## Script Selection

When OCR mode is active, a floating toolbar slides down from the top with four script buttons:
- **עברי** — standard Hebrew (Tesseract `heb` model)
- **רש"י** — Rashi script (Tesseract `heb_rashi` model)
- **מעורב** — mixed: both `heb+heb_rashi` combined
- **English** — Latin script (Tesseract `eng` model); the result textarea switches to LTR for this script

Script selection is synced between `pdfOcrStore.script` and `usePdfOcrSelection.script`. Changing the script:
1. Updates the composable's script ref
2. Preloads the Tesseract worker for that script
3. Updates the injected iframe's language setting for future selections

## Integration Points

**Title bar (`src/layout/AppTitleBar.vue`):**
- Renders the OCR button only on PDF tabs (`activeTab?.route === '/pdf-view'`)
- Button is hidden if `settingsStore.titleBarHiddenButtons` includes `'ocr'`
- Calls `pdfOcrStore.toggle()` on click

**Stores:**
- `pdfOcrStore` — owns all OCR UI state; shared with `PdfViewPage` and composable
- `localFileStore` — manages PDF file virtual host; used by `PdfViewPage` to set iframe `src`
- `tabStore` — tracks PDF toolbar visibility setting per tab

## Performance & Resource Management

**Tesseract workers:**
- Initialized on first use (not on app boot) — reduces cold-start parse cost
- Workers persist in memory across multiple OCR operations
- Cleaned up in `onUnmounted` when the PDF tab closes
- Language model files (`heb.traineddata`, `heb_rashi.traineddata`, `eng.traineddata`) loaded from `/tesseract/` public folder

**Iframe:**
- Aggressively torn down in `onBeforeUnmount` — iframe is set to `about:blank` and removed to release the PDF.js worker, canvases, and WebView2 sub-frame immediately

**Popup:**
- Uses `v-if` so the component unmounts entirely when dismissed
- Text is editable but not persisted — only in memory during the session

## Accessibility & RTL

- All text in Hebrew (no English user-facing strings)
- Popup uses `dir="rtl"` explicitly
- Button labels are right-aligned in the textarea
- Escape key dismisses the popup
- Tab-trappable via focus management in the modal overlay

## Known Limitations

- Text extraction requires a text layer in the PDF — scanned PDFs must be OCR'd (no fallback to image-based search)
- Tesseract OCR quality depends on image resolution and script clarity; Rashi script can be particularly challenging
- OCR is slow on large selections or low-end devices — progress bar provides visual feedback
- Cannot select across multiple pages — rectangle is drawn on a single viewport
