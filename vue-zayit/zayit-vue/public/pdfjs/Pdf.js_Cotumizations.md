# PDF.js Customizations

This PDF.js installation includes several customizations for optimal performance, visual quality, and Hebrew localization in the Zayit Vue app.

## Hebrew Localization

### Problem

PDF.js by default uses the browser's language setting (`navigator.language`), which may not be Hebrew. The viewer needs to be hardcoded to Hebrew locale for consistent RTL layout and Hebrew UI text.

### Solution

Modified PDF.js's internal locale detection to read from URL parameter with Hebrew as default.

### Implementation

**File**: `public/pdfjs/web/viewer.mjs` (line ~652)

Modified the `localeProperties` configuration:

```javascript
localeProperties: {
  value: {
    lang: new URLSearchParams(window.location.search).get("locale") || "he"
  },
  kind: OptionKind.BROWSER
},
```

**Original code:**

```javascript
localeProperties: {
  value: {
    lang: navigator.language || "en-US"
  },
  kind: OptionKind.BROWSER
},
```

### How It Works

1. PDF.js reads the `locale` URL parameter (e.g., `viewer.html?locale=he`)
2. Falls back to `"he"` if no parameter is provided
3. Loads Hebrew translations from `locale/he/viewer.ftl`
4. Automatically sets `dir="rtl"` because Hebrew is in PDF.js's RTL languages list: `["ar", "he", "fa", "ps", "ur"]`
5. Applies Hebrew locale to all UI elements (tooltips, buttons, menus)

### Vue App Integration

Both PDF viewer components pass the locale parameter:

**Files:**

- `src/components/pages/PdfViewPage.vue`
- `src/components/pages/HebrewBooksViewPage.vue`

```javascript
params.set("locale", "he");
```

### Benefits

- **Consistent Hebrew UI** - All tooltips and buttons display in Hebrew
- **Automatic RTL layout** - PDF.js detects Hebrew and applies right-to-left layout
- **Proper page spreading** - Pages spread right-to-left for Hebrew books
- **URL parameter support** - Can override locale if needed (e.g., `?locale=en-US`)
- **Uses PDF.js internal methods** - No external scripts or hacks required
- **Complete sidebar translations** - Added missing "views-manager" translations for the newer sidebar interface

### Missing Translations Fixed

The original Hebrew locale file (`locale/he/viewer.ftl`) was missing translations for the newer "views-manager" sidebar interface introduced in recent PDF.js versions. We added the following translations:

- Sidebar toggle button labels
- View selector (Pages, Outline, Attachments, Layers)
- Page management actions (Copy, Cut, Delete, Save as)
- Status messages for undo operations
- Warning messages for failed operations

**Translation style:** Used declarative "חלונית צד" (side panel) instead of action-oriented phrases for better Hebrew UX.

### References

- [PDF.js Issue #11829](https://github.com/mozilla/pdf.js/issues/11829) - Setting locale doesn't work
- [StackOverflow Solution](https://stackoverflow.com/questions/64915575/how-to-force-set-locale-in-pdf-js) - How to force locale in PDF.js

## Theme Synchronization

### PDF.js Native Theme System

PDF.js has a built-in theme system controlled by the `viewerCssTheme` option:

- `0` = Auto (follows system preference)
- `1` = Light theme
- `2` = Dark theme

### Vue App Integration

The Vue app's theme utility (`zayit-vue/src/utils/theme.ts`) automatically syncs themes by:

1. **Detecting PDF.js iframes** - Finds all iframes with `/pdfjs/web/viewer.html` in their src
2. **Accessing PDF.js API** - Uses `window.PDFViewerApplicationOptions` exposed by PDF.js
3. **Setting theme directly** - Calls `AppOptions.set('viewerCssTheme', themeValue)`
4. **Applying color-scheme** - Sets CSS `color-scheme` property like PDF.js does internally

### Implementation Details

```javascript
// In Vue app theme.ts
const iframeWindow = iframe.contentWindow;
const AppOptions = iframeWindow.PDFViewerApplicationOptions;

// Set PDF.js theme: 1 = light, 2 = dark
const themeValue = isDark ? 2 : 1;
AppOptions.set("viewerCssTheme", themeValue);

// Set color-scheme property
const docStyle = iframeWindow.document.documentElement.style;
docStyle.setProperty("color-scheme", isDark ? "dark" : "light");
```

### Automatic Sync

Theme synchronization happens automatically:

- **On app initialization** - `initTheme()` syncs existing PDF viewers
- **On theme toggle** - `toggleTheme()` immediately syncs all PDF viewers
- **On iframe load** - PDF viewer components call `syncPdfViewerTheme()` when loaded
- **On dynamic content** - MutationObserver detects new PDF iframes and syncs them

## Sharpness Enhancement

### Problem

PDF.js renders at low resolution on displays with `devicePixelRatio` of 1.0, causing blurry text and graphics, especially when zoomed out on small viewports.

### Solution

Override `window.devicePixelRatio` to force high-DPI rendering before PDF.js initializes.

### Implementation

**File**: `public/pdfjs/web/pixel-ratio-override.js`

```javascript
// Force higher pixel ratio for sharper rendering
const originalDevicePixelRatio = window.devicePixelRatio || 1;
const enhancedPixelRatio = Math.max(originalDevicePixelRatio, 2); // Minimum 2x

// Override devicePixelRatio property
Object.defineProperty(window, "devicePixelRatio", {
  get: function () {
    return enhancedPixelRatio;
  },
  configurable: true,
});
```

**Integration**: Added to `viewer.html` before PDF.js loads:

```html
<!-- Override devicePixelRatio for sharper rendering - MUST load first -->
<script src="pixel-ratio-override.js"></script>
```

### How It Works

1. PDF.js `OutputScale` class uses `globalThis.devicePixelRatio || 1`
2. Canvas dimensions calculated as `width * outputScale.sx` where `outputScale.sx = pixelRatio`
3. By forcing `devicePixelRatio` ≥ 2, all PDF canvases render at 2x+ resolution
4. Provides same sharp rendering as Edge's built-in viewer

### Benefits

- **Sharp text and graphics** at all zoom levels
- **Automatic scaling** - PDF.js handles high-DPI rendering internally
- **No performance impact** on high-DPI displays (already using high pixel ratio)
- **Universal improvement** - Works for all PDF content

## Performance Optimization

### Problem

Large PDF files load slowly, with pages taking time to appear when scrolling.

### Solution

Enable PDF.js built-in performance optimizations via URL parameters.

### Implementation

**Files**:

- `src/components/pages/PdfViewPage.vue` (regular PDF viewer)
- `src/components/pages/HebrewBooksViewPage.vue` (HebrewBooks PDF viewer)

```javascript
// Performance optimizations for large files
params.set("disableAutoFetch", "false"); // Enable auto-fetch for better performance
params.set("disableStream", "false"); // Enable streaming for faster loading
params.set("disableRange", "false"); // Enable range requests for partial loading
params.set("enableHWA", "true"); // Enable hardware acceleration
params.set("cMapPacked", "true"); // Use packed CMaps for faster font loading
```

### Optimizations Explained

1. **Range Requests** (`disableRange=false`)
   - Downloads only needed parts of PDF
   - Faster initial loading, loads visible pages first
   - Critical for large files

2. **Streaming** (`disableStream=false`)
   - Progressive loading of PDF data
   - PDF starts rendering before fully downloaded
   - Reduces perceived loading time

3. **Auto-Fetch** (`disableAutoFetch=false`)
   - Automatically fetches pages in background
   - Pages load faster when user scrolls
   - Improves navigation experience

4. **Hardware Acceleration** (`enableHWA=true`)
   - Uses GPU for rendering when available
   - Faster page rendering and smoother scrolling
   - Better performance on complex pages

5. **Packed CMaps** (`cMapPacked=true`)
   - Compressed font character mappings
   - Faster font loading, especially for non-Latin text
   - Reduces font-related delays

### Benefits

- **Faster initial loading** - Pages appear progressively
- **Smoother scrolling** - Background loading and GPU acceleration
- **Better large file handling** - Only loads what's needed
- **Improved font performance** - Especially important for Hebrew text

## Rectangle Selection Tool

### Problem

Multi-column PDFs often have OCR issues where text from different columns gets mixed together, making it impossible to select text from a single column using normal text selection. Additionally, some PDFs are scanned images with no text layer at all.

### Solution

Added a custom rectangle selection tool that allows users to draw a rectangle around a specific area to select only the text within that region. The tool shows an editable popup with the selected text, allowing users to review and modify before copying. If no text layer is found, the tool automatically falls back to OCR (Tesseract.js) to extract text from the image.

### Implementation

**Files**:

- `public/pdfjs/web/rectangle-selection.js` - New standalone tool with OCR fallback
- `public/pdfjs/web/viewer.html` - Added script reference
- `package.json` - Added tesseract.js dependency

### How It Works

1. **Toggle Button** - Added to the right toolbar with a dashed rectangle icon
2. **Drawing Mode** - Click the button to activate crosshair cursor
3. **Rectangle Selection** - Click and drag to draw a selection rectangle
4. **Word Detection** - Identifies all words (spans) whose center point falls within the rectangle
5. **OCR Fallback** - If no text found, captures the rectangle area as an image and performs OCR
6. **Editable Popup** - Shows selected/recognized text in an editable textarea with RTL support
7. **Copy or Cancel** - User can edit the text and copy, or cancel the operation
8. **Auto-Deactivate** - Tool deactivates after making a selection

### Features

- **Visual Feedback** - Dashed blue rectangle shows selection area while drawing
- **Crosshair Cursor** - Clear indication when tool is active
- **Word-Level Selection** - Leverages PDF.js's word-based text layer (each word is a separate span)
- **OCR Fallback** - Automatically uses Tesseract.js OCR when no text layer exists
- **Hebrew OCR** - Tesseract worker configured specifically for Hebrew text recognition
- **Loading Indicator** - Shows spinner with "מבצע OCR..." message during OCR processing
- **Editable Preview** - Review and modify selected/recognized text before copying
- **RTL Support** - Textarea uses right-to-left direction for Hebrew text
- **Keyboard Support** - Press Escape to close popup, Enter in textarea for line breaks
- **Click Outside to Close** - Click overlay to dismiss popup
- **Scroll-Aware** - Works correctly with scrolled content
- **Multi-Page Support** - Handles text across multiple visible pages
- **High-DPI OCR** - Captures at canvas internal resolution for better OCR accuracy

### Technical Details

**Word Detection Algorithm**:

```javascript
// Check if span center is within rectangle
const spanCenterX = spanLeft + spanRect.width / 2;
const spanCenterY = spanTop + spanRect.height / 2;

const isInRect =
  spanCenterX >= rect.left &&
  spanCenterX <= rect.left + rect.width &&
  spanCenterY >= rect.top &&
  spanCenterY <= rect.top + rect.height;
```

**OCR Fallback Process**:

1. Check if any text spans found within rectangle
2. If no text found, locate the PDF canvas that intersects with the rectangle
3. Calculate rectangle position relative to canvas (accounting for scroll)
4. Get canvas scale factor (internal resolution vs displayed size)
5. Create cropped canvas with the rectangle area at full resolution
6. Pass cropped canvas to Tesseract.js worker configured for Hebrew
7. Display recognized text in popup with "טקסט מזוהה (OCR)" title

**Why Word-Level Instead of Character-Level**:

- PDF.js text layer wraps each word in its own `<span>` element
- Checking word centers is much faster than checking individual characters
- Browser selection API cannot select non-continuous text (words with gaps)
- Direct clipboard copy is the only way to get ONLY the words within rectangle bounds

**Tesseract.js Integration**:

- Loaded from CDN (https://cdn.jsdelivr.net/npm/tesseract.js@5)
- Worker initialized with Hebrew language pack ('heb')
- Runs offline after initial download of language data
- Processes at full canvas resolution for better accuracy

**Popup Features**:

- Modal overlay with semi-transparent background
- Centered popup with white background and shadow
- Dynamic title: "טקסט נבחר" for text layer, "טקסט מזוהה (OCR)" for OCR results
- Editable textarea with RTL direction and Hebrew alignment
- "העתק" (Copy) button - copies text to clipboard and closes popup
- "ביטול" (Cancel) button - closes popup without copying
- Escape key handler for quick dismissal
- Click-outside-to-close functionality

### Usage

1. Click the rectangle selection button in the toolbar (dashed rectangle icon)
2. Click and drag to draw a rectangle around the desired text area
3. Release to see the popup with selected text
   - If text layer exists: Shows extracted text immediately
   - If no text layer: Shows "מבצע OCR..." spinner, then recognized text
4. Edit the text if needed
5. Click "העתק" to copy to clipboard, or "ביטול" to cancel

### Benefits

- **Column-Specific Selection** - Select text from a single column in multi-column layouts
- **OCR Problem Workaround** - Bypass OCR text ordering issues
- **Scanned PDF Support** - Extract text from image-only PDFs using OCR
- **Precise Selection** - Select exactly the text you need
- **Review Before Copy** - See and edit what you're copying
- **User-Friendly** - Simple click-and-drag interface with clear visual feedback
- **Hebrew-Optimized** - RTL text direction, Hebrew button labels, and Hebrew OCR
- **Offline OCR** - Works without internet after initial language data download
- **High Accuracy** - Uses full resolution canvas for better OCR results

### Performance Notes

- **First Use**: Tesseract.js downloads Hebrew language data (~2-3 MB) on first initialization
- **Subsequent Uses**: Language data cached, OCR runs offline
- **OCR Speed**: Typically 1-3 seconds depending on rectangle size and text complexity
- **Memory**: Tesseract worker stays in memory for faster subsequent OCR operations

## Sidebar Customization (חלונית צד)

### Problem

The default PDF.js sidebar (חלונית צד) has a floating appearance with rounded corners and doesn't fill the full height. Additionally, there's a redundant status section showing between the view selector and content, and the default view is pages instead of the more useful outline (תוכן עניינים).

### Solution

1. Added custom CSS to make the sidebar fill the full height from toolbar to bottom
2. Removed all rounded corners for a more integrated appearance
3. Hidden the redundant status section (only shows when there are actual actions, warnings, or loading states)
4. Changed the default sidebar view to outline (תוכן עניינים) instead of pages
5. Reordered the view menu to show outline first
6. Simplified the Hebrew translation from "תוכן עניינים של המסמך" to "תוכן עניינים"

### Implementation

**Files Modified**:

- `public/pdfjs/web/viewer-custom.css` - New custom stylesheet
- `public/pdfjs/web/viewer.html` - Added custom stylesheet reference and reordered view menu
- `public/pdfjs/web/viewer.mjs` (line ~16764) - Changed default active view to OUTLINE
- `public/pdfjs/web/locale/he/viewer.ftl` - Simplified Hebrew translation

**Custom CSS** (`viewer-custom.css`):

```css
/* Make sidebar fill full height from toolbar to bottom, no rounded corners */
#viewsManager {
  /* Full height from toolbar to bottom */
  height: calc(100vh - var(--toolbar-height)) !important;
  top: var(--toolbar-height) !important;
  bottom: 0 !important;
  padding-bottom: 0 !important;

  /* Remove rounded corners */
  border-radius: 0 !important;
}

/* Hide only the default status action section (showing view name) */
/* Keep visible when there are actual actions, warnings, or loading states */
#viewsManagerStatusAction {
  display: none !important;
}

/* Remove rounded corners from sidebar buttons */
#viewsManager .viewsManagerButton {
  border-radius: 0 !important;
}
```

**JavaScript Modification** (viewer.mjs):

```javascript
// In ViewsManager constructor
this.isOpen = false;
this.active = SidebarView.OUTLINE; // Custom: Default to outline view instead of thumbs
this.isInitialViewSet = false;
```

**HTML Modification** (viewer.html):

Reordered the view selector menu to show outline first:

```html
<menu id="viewsManagerSelectorOptions">
  <li>
    <button id="outlinesViewMenu">תוכן עניינים</button>
  </li>
  <li>
    <button id="thumbnailsViewMenu">עמודים</button>
  </li>
  <!-- ... other views ... -->
</menu>
```

**Hebrew Translation** (locale/he/viewer.ftl):

```
pdfjs-views-manager-outlines-title = תוכן עניינים
pdfjs-views-manager-outlines-option-label = תוכן עניינים
```

### How It Works

1. **Full Height** - CSS overrides make the sidebar extend from the toolbar to the bottom of the viewport
2. **No Rounded Corners** - All border-radius properties are set to 0
3. **Smart Status Section** - The status area is hidden by default but shows when needed:
   - Hidden: Default state showing just the view name
   - Visible: When there are action buttons, undo operations, warnings, or loading messages
4. **Default to Outline** - When sidebar opens, it shows outline (תוכן עניינים) by default if available
5. **Automatic Fallback** - If no outline exists, PDF.js automatically falls back to pages view
6. **Menu Order** - Outline appears first in the view selector dropdown for easier access

### Benefits

- **Integrated Appearance** - Sidebar looks like part of the main interface, not floating
- **Full Space Utilization** - Uses all available vertical space
- **Clean Interface** - No redundant UI elements cluttering the view
- **Contextual Information** - Status section appears only when there's something meaningful to show
- **Better Navigation** - Defaults to the most useful view (outline) when available
- **Consistent Design** - Matches the overall application design language
- **Clearer Labels** - Simplified Hebrew text is more concise and easier to read

## Rectangle Selection Tool

## Save Dialog Enhancement

### Problem

PDF.js automatically downloads files without showing a save dialog, giving users no control over the download location or filename. Additionally, when loading PDFs via blob URLs, the document properties (מאפייני מסמך) don't show the filename because blob URLs don't contain filename information.

### Solution

1. Modified the download function to use the File System Access API when available, showing a native save dialog
2. Added support for a custom `filename` URL parameter to pass the original filename to PDF.js

### Implementation

**Files Modified**:

- `public/pdfjs/web/viewer.mjs` - Added filename parameter support and save dialog
- `src/components/pages/PdfViewPage.vue` - Passes filename parameter
- `src/components/pages/HebrewBooksViewPage.vue` - Passes filename parameter

**Part 1: Filename Parameter Support (viewer.mjs line ~17484)**

Added code to read a custom `filename` URL parameter and set it as the content disposition filename:

```javascript
// Custom: Handle filename parameter for blob URLs
const customFilename = params.get("filename");
if (customFilename) {
  this._contentDispositionFilename = decodeURIComponent(customFilename);
  console.log(
    "[PDF.js] Custom filename from URL parameter:",
    this._contentDispositionFilename,
  );
}
```

**Part 2: Save Dialog (viewer.mjs line ~5540)**

Modified the `download()` function to use `showSaveFilePicker()`:

```javascript
async function download(blobUrl, filename) {
  // Debug logging
  console.log("[PDF.js Download] Filename received:", filename);

  const suggestedFilename = filename || "document.pdf";

  // Try to use File System Access API for save dialog (Chrome/Edge)
  if (window.showSaveFilePicker) {
    try {
      const response = await fetch(blobUrl);
      const blob = await response.blob();

      const handle = await window.showSaveFilePicker({
        suggestedName: suggestedFilename,
        types: [
          {
            description: "PDF Files",
            accept: { "application/pdf": [".pdf"] },
          },
        ],
      });

      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();

      if (blobUrl.startsWith("blob:")) {
        URL.revokeObjectURL(blobUrl);
      }
      return;
    } catch (err) {
      // Falls back to default behavior if user cancels or error occurs
    }
  }

  // Fallback for browsers without File System Access API
  // ... original download code ...
}
```

**Part 3: Vue Components Pass Filename**

```javascript
// Pass filename for proper document properties and save dialog
if (tab?.pdfState?.fileName) {
  params.set("filename", encodeURIComponent(tab.pdfState.fileName));
}
```

### How It Works

1. **Filename Parameter** - Vue components pass the original filename via URL parameter (e.g., `?filename=mybook.pdf`)
2. **PDF.js Reads Filename** - PDF.js reads the parameter and sets `_contentDispositionFilename`
3. **Document Properties** - The filename appears in document properties (מאפייני מסמך)
4. **Save Dialog** - When user clicks download/save:
   - Checks if `window.showSaveFilePicker` is available
   - Shows native OS save dialog with the original filename pre-filled
   - User can choose location and modify filename
   - File is written to chosen location
5. **Graceful Fallback** - Falls back to automatic download if:
   - User cancels the dialog
   - Browser doesn't support the API
   - Any error occurs

### Browser Support

- **Supported**: Chrome 86+, Edge 86+, Opera 72+
- **Fallback**: Firefox, Safari, and older browsers use automatic download

### Benefits

- **Proper Document Properties** - Filename shows correctly in מאפייני מסמך even with blob URLs
- **User Control** - Choose where to save files
- **Filename Editing** - Modify filename before saving
- **No Duplicate Downloads** - Prevents accidental duplicate files in Downloads folder
- **Better UX** - Matches expected behavior from desktop PDF viewers
- **Graceful Degradation** - Works in all browsers with appropriate fallback

### References

- [File System Access API](https://developer.mozilla.org/en-US/docs/Web/API/File_System_Access_API)
- [showSaveFilePicker](https://developer.mozilla.org/en-US/docs/Web/API/Window/showSaveFilePicker)

## Rectangle Selection Tool

## Files Modified

### Sidebar Customization

- **viewer.html** - Added `viewer-custom.css` stylesheet and reordered view menu (outline first)
- **viewer-custom.css** - New file with custom sidebar styles (full height, no rounded corners, hidden redundant status)
- **viewer.mjs** (line ~16764) - Changed default active view from THUMBS to OUTLINE
- **locale/he/viewer.ftl** - Simplified Hebrew translation for outline view

### Save Dialog Enhancement

- **viewer.mjs** (line ~17484) - Added custom `filename` URL parameter support
- **viewer.mjs** (line ~5540) - Modified `download()` function to use File System Access API for save dialog
- **PdfViewPage.vue** - Passes `filename` parameter to PDF.js viewer
- **HebrewBooksViewPage.vue** - Passes `filename` parameter to PDF.js viewer

### Rectangle Selection Tool

- **viewer.html** - Added `rectangle-selection.js` script reference
- **rectangle-selection.js** - New file with rectangle selection implementation

### Hebrew Localization

- **viewer.mjs** (line ~652) - Modified `localeProperties` to read from URL parameter with Hebrew default
- **locale/he/viewer.ftl** - Added missing "views-manager" translations for sidebar interface
- **PdfViewPage.vue** - Passes `locale=he` parameter
- **HebrewBooksViewPage.vue** - Passes `locale=he` parameter

### Theme System

- **viewer.html** - No modifications needed (removed CSS override link)
- **theme.ts** - Enhanced with PDF.js native theme API calls
- **Vue components** - Already call `syncPdfViewerTheme()` on iframe load

### Sharpness Enhancement

- **viewer.html** - Added `pixel-ratio-override.js` script
- **pixel-ratio-override.js** - New file with devicePixelRatio override

### Performance Optimization

- **PdfViewPage.vue** - Added performance URL parameters
- **HebrewBooksViewPage.vue** - Added performance URL parameters

## Debugging

### Theme Sync

Use browser console to debug theme sync:

```javascript
// Check current theme
window.zayitTheme.current();

// Force sync all PDF viewers
window.zayitTheme.sync();

// Toggle theme
window.zayitTheme.toggle();
```

### Sharpness Enhancement

Check console for pixel ratio override confirmation:

```
[PDF.js Enhancement] Original devicePixelRatio: 1, Enhanced: 2
```

### Performance Optimization

Check browser Network tab to verify:

- Range requests are being used (partial content responses)
- Streaming is active (progressive loading)
- Only visible pages are initially loaded

## References

- **Sharpness Solution**: [StackOverflow - PDF.js Low Resolution Fix](https://stackoverflow.com/questions/49426385/how-to-fix-pdf-documents-from-being-rendered-in-really-low-resolution-blurry)
- **PDF.js Options**: Built-in `AppOptions` system in PDF.js viewer
- **Performance Settings**: PDF.js URL parameter documentation
