# pdf

PDF and Word document viewer.

**PdfViewPage.vue** — renders the PDF.js viewer in an iframe served via a C# virtual host. Handles local files, HebrewBooks downloads, and Word-to-PDF conversions. Session restore is handled by `pdfStore` at app boot — do not add restore logic here.
