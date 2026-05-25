# html-view

HTML file viewer for displaying local HTML and HTM files.

**HtmlViewPage.vue** — renders HTML files in an iframe served via a C# virtual host. Handles local HTML/HTM files opened via the file picker. Session restore is handled by `localFileStore` at app boot — do not add restore logic here. Displays a loading state with a 6-second timeout; if the iframe fails to load within that time, shows an error message with a retry button. Supports the PDF page filters setting for visual adjustments.
