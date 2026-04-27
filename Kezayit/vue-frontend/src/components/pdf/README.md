# pdf

PDF and Word document viewer.

**PdfViewPage.vue** — renders the PDF.js viewer in an iframe served via a C# virtual host. Handles local files, HebrewBooks downloads, and Word-to-PDF conversions. Session restore is handled by `pdfStore` at app boot — do not add restore logic here.

**PdfToolbar.vue** — compact 44px Android-friendly toolbar that overlays the iframe. Hides the native PDF.js toolbar (via `viewer-custom.css`) and replaces it with: sidebar toggle, find toggle, prev/next page, page number input, zoom out/in/select, and a "more" dropdown (download, rectangle selection). Communicates with the iframe via `contentWindow.PDFViewerApplication`. Syncs page number and zoom state by polling every 400ms.
