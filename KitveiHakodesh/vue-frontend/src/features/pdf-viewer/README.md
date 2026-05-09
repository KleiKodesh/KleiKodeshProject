# pdf-viewer

PDF viewer with OCR text extraction.

**PdfViewPage.vue** — renders the PDF.js viewer in an iframe served via a C# virtual host. Handles local files, HebrewBooks downloads, and Word-to-PDF conversions. Session restore is handled by `pdfStore` at app boot — do not add restore logic here. Displays a conversion progress overlay while files are being processed.

**usePdfOcrSelection.ts** — composable that manages OCR text extraction. Injects a selection tool into the PDF.js iframe, captures user-drawn rectangles, attempts text extraction from the text layer first, and falls back to Tesseract.js OCR on canvas data if needed. Supports Hebrew and Rashi scripts. Returns extracted text via `result` ref.

**PdfOcrResultPopup.vue** — modal popup displaying OCR results. Shows extracted text in an editable textarea, allows script switching (Hebrew/Rashi), and provides copy-to-clipboard functionality. Dismisses on overlay click or Escape key.

**pdfOcrInjectedScript.ts** — injected script that runs inside the PDF.js iframe. Implements the selection rectangle UI (crosshair cursor, dashed selection box), text layer hit testing, and canvas capture. Communicates back to the parent window via postMessage.
