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

### PDF.js Native Theme System with Zayit Integration

PDF.js has a built-in theme system that we've extended to support Zayit's custom themes:

- `0` = Auto (follows system preference)
- `1` = Light theme
- `2` = Dark theme

### Zayit Theme Integration

**How it works:**

1. **PDF.js CSS Modified** - `viewer.css` now uses CSS variables with fallbacks:

   ```css
   --toolbar-bg-color: var(
     --bg-primary-custom,
     light-dark(rgb(249 249 250), rgb(56 56 61))
   );
   --main-color: var(
     --text-primary-custom,
     light-dark(rgb(12 12 13), rgb(249 249 250))
   );
   ```

2. **Vue App Injects Variables** - `themes.ts` copies theme variables into PDF.js iframe:

   ```javascript
   iframeRootStyle.setProperty("--bg-primary-custom", theme.ui.bgPrimary);
   iframeRootStyle.setProperty("--text-primary-custom", theme.ui.textPrimary);
   ```

3. **Reactive Updates** - CSS variables are live bindings, so theme changes instantly update PDF.js

### Theme Variables Mapped

| Zayit Variable          | PDF.js Usage                        |
| ----------------------- | ----------------------------------- |
| `--bg-primary-custom`   | Toolbar, fields, dialogs            |
| `--bg-secondary-custom` | Body background, sidebar, dropdowns |
| `--text-primary-custom` | Main text, icons, buttons           |
| `--border-color-custom` | Borders, separators                 |
| `--accent-color-custom` | Progress bar, highlights            |
| `--hover-bg-custom`     | Button hover states                 |

### Vue App Integration

The Vue app's theme utility (`zayit-vue/src/utils/themes.ts`) automatically syncs themes by:

1. **Detecting PDF.js iframes** - Finds all iframes with `/pdfjs/web/viewer.html` in their src
2. **Setting native theme mode** - Calls `AppOptions.set('viewerCssTheme', themeValue)` for light-dark() support
3. **Injecting CSS variables** - Copies all `--*-custom` variables from parent document to iframe
4. **Setting color-scheme** - Sets CSS `color-scheme` property for native light-dark() function

### Implementation Details

```javascript
// In Vue app themes.ts
const iframeWindow = iframe.contentWindow;
const AppOptions = iframeWindow.PDFViewerApplicationOptions;

// Set PDF.js native theme mode: 1 = light, 2 = dark
const themeValue = isDark ? 2 : 1;
AppOptions.set("viewerCssTheme", themeValue);

// Set color-scheme for light-dark() CSS function
const iframeDoc = iframeWindow.document;
iframeDoc.documentElement.style.setProperty(
  "color-scheme",
  isDark ? "dark" : "light",
);

// Add/remove dark class for scrollbar and UI theming
if (isDark) {
  iframeDoc.documentElement.classList.add("dark");
} else {
  iframeDoc.documentElement.classList.remove("dark");
}

// Inject Zayit theme variables
const themeVars = [
  "--bg-primary-custom",
  "--bg-secondary-custom",
  "--text-primary-custom",
  "--text-secondary-custom",
  "--border-color-custom",
  "--accent-color-custom",
  "--hover-bg-custom",
  "--active-bg-custom",
];
themeVars.forEach((varName) => {
  const value = document.documentElement.style.getPropertyValue(varName);
  if (value) {
    iframeDoc.documentElement.style.setProperty(varName, value);
  }
});
```

### Automatic Sync

Theme synchronization happens automatically:

- **On app initialization** - `initTheme()` syncs existing PDF viewers
- **On theme toggle** - `toggleTheme()` immediately syncs all PDF viewers
- **On iframe load** - PDF viewer components call `syncPdfViewerTheme()` when loaded
- **On dynamic content** - MutationObserver detects new PDF iframes and syncs them

### Benefits

- **100% Theme Matching** - PDF.js uses exact same colors as your Vue app
- **Real-time Updates** - Theme changes instantly reflect in PDF viewer (no reload needed)
- **No Hacks** - Clean integration using PDF.js's own CSS variable system
- **Fallback Support** - PDF.js defaults work if variables aren't injected
- **All 36 Themes** - Every Zayit theme automatically works in PDF.js

### Complete Theme Coverage

The integration covers all PDF.js UI elements:

1. **Toolbar** - Background, borders, buttons, icons
2. **Sidebar** - Background, borders, content area
3. **Dropdown Menus** - Background, text, borders (views manager selector)
4. **Scrollbars** - Semi-transparent overlays that adapt to theme backgrounds
5. **Fields & Inputs** - Background, text, borders
6. **Dialogs** - Background, buttons, hover states
7. **Progress Bar** - Uses accent color from theme

### Files Modified

**1. viewer.css** (line ~7112-7145)

- Modified `:root` color variables to use `var(--*-custom, fallback)` pattern
- Maps PDF.js variables to Zayit theme variables

**2. viewer-custom.css**

- Overrides scrollbar variables with semi-transparent rgba values
- Adds `dark` class support for scrollbar theming
- Overrides sidebar colors to use theme variables
- Overrides popup menu colors for dropdown theming

**3. themes.ts** (syncPdfViewerTheme function)

- Sets `color-scheme` property for light-dark() support
- Adds/removes `dark` class on iframe document element
- Injects 8 theme CSS variables into iframe:
  - `--bg-primary-custom`
  - `--bg-secondary-custom`
  - `--text-primary-custom`
  - `--text-secondary-custom`
  - `--border-color-custom`
  - `--accent-color-custom`
  - `--hover-bg-custom`
  - `--active-bg-custom`

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

Added a custom rectangle selection tool that allows users to draw a rectangle around a specific area to select only the text within that region. The tool shows an editable popup with the selected text, allowing users to review and modify before copying. If no text layer is found, the tool automatically uses OCR to extract text from the image, with a smart fallback chain for best results.

### Implementation

**Files**:

- `public/pdfjs/web/rectangle-selection.js` - New standalone tool with dual-mode OCR
- `public/pdfjs/web/viewer.html` - Added script reference
- `package.json` - Added tesseract.js dependency
- `public/pdfjs/tesseract/heb.traineddata` - Hebrew language data for offline OCR (must be downloaded separately)

### How It Works

1. **Toggle Button** - Added to the right toolbar with a dashed rectangle icon
2. **Drawing Mode** - Click the button to activate crosshair cursor
3. **Rectangle Selection** - Click and drag to draw a selection rectangle
4. **Word Detection** - Identifies all words (spans) whose center point falls within the rectangle
5. **Dual-Mode OCR Fallback** - If no text found:
   - **First**: Try OCR.space API (online AI OCR) - superior quality
   - **Fallback**: Use local Tesseract.js if online fails
6. **Editable Popup** - Shows selected/recognized text in an editable textarea with RTL support
7. **Copy or Cancel** - User can edit the text and copy, or cancel the operation
8. **Auto-Deactivate** - Tool deactivates after making a selection

### Features

- **Visual Feedback** - Dashed blue rectangle shows selection area while drawing
- **Crosshair Cursor** - Clear indication when tool is active
- **Word-Level Selection** - Leverages PDF.js's word-based text layer (each word is a separate span)
- **Dual-Mode OCR** - Online AI OCR when internet available, offline Tesseract as backup
- **Hebrew OCR** - Both engines configured specifically for Hebrew text recognition
- **Loading Indicator** - Shows spinner with "מבצע OCR..." message during OCR processing
- **Editable Preview** - Review and modify selected/recognized text before copying
- **RTL Support** - Textarea uses right-to-left direction for Hebrew text
- **Keyboard Support** - Press Escape to close popup, Enter in textarea for line breaks
- **Click Outside to Close** - Click overlay to dismiss popup
- **Scroll-Aware** - Works correctly with scrolled content
- **Multi-Page Support** - Handles text across multiple visible pages
- **High-DPI OCR** - Captures at canvas internal resolution for better OCR accuracy

### OCR Modes

**Mode 1: Online AI OCR (OCR.space)**

- Uses OCR.space API with Engine 2 (optimized for Hebrew)
- Superior accuracy compared to Tesseract
- Handles poor quality scans better
- Auto-detects text orientation
- Better at mixed Hebrew/English text
- Requires internet connection
- Uses public demo API key (shared rate limits)
- **Future Enhancement**: Allow users to configure their own API key in settings for better rate limits

**Mode 2: Offline OCR (Tesseract.js)**

- Runs completely offline using local Hebrew language data
- No rate limits or API dependencies
- Privacy-friendly (images never leave the computer)
- Always available as fallback
- Good accuracy for most Hebrew text
- Requires `heb.traineddata` file (~1.8 MB) to be downloaded and placed in `public/pdfjs/tesseract/`

**Fallback Chain:**

```
Text Layer Found? → Use text layer (instant)
    ↓ No
Internet Available? → Try OCR.space API (better quality)
    ↓ Failed/No Internet
Use Tesseract.js → Always works offline
```

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
6. Try OCR.space API first (if internet available)
7. If online OCR fails, use Tesseract.js worker configured for Hebrew
8. Display recognized text in popup with "טקסט מזוהה (OCR)" title

**Why Word-Level Instead of Character-Level**:

- PDF.js text layer wraps each word in its own `<span>` element
- Checking word centers is much faster than checking individual characters
- Browser selection API cannot select non-continuous text (words with gaps)
- Direct clipboard copy is the only way to get ONLY the words within rectangle bounds

**OCR.space Integration**:

- API endpoint: https://api.ocr.space/parse/image
- Uses public demo API key (shared across all users)
- Engine 2 selected for better Hebrew support
- Includes orientation detection and auto-scaling
- Gracefully handles failures (network errors, rate limits, API downtime)

**Tesseract.js Integration**:

- Loaded from CDN (https://cdn.jsdelivr.net/npm/tesseract.js@5)
- Worker initialized with Hebrew language pack ('heb')
- Language data loaded from local file (`/pdfjs/tesseract/heb.traineddata`)
- Runs offline after initial setup
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

### Setup Requirements

**Hebrew Language Data:**

Download the Hebrew trained data file for offline OCR:

1. Download: https://github.com/tesseract-ocr/tessdata_fast/raw/4.1.0/heb.traineddata
2. Save to: `zayit-vue/public/pdfjs/tesseract/heb.traineddata`
3. File size: ~1.8 MB

Without this file, only online OCR (OCR.space) will work.

### Usage

1. Click the rectangle selection button in the toolbar (dashed rectangle icon)
2. Click and drag to draw a rectangle around the desired text area
3. Release to see the popup with selected text:
   - **Text layer exists**: Shows extracted text immediately
   - **No text layer + internet**: Shows "מבצע OCR..." spinner, then OCR.space result
   - **No text layer + no internet**: Shows "מבצע OCR..." spinner, then Tesseract result
4. Edit the text if needed
5. Click "העתק" to copy to clipboard, or "ביטול" to cancel

### Benefits

- **Column-Specific Selection** - Select text from a single column in multi-column layouts
- **OCR Problem Workaround** - Bypass OCR text ordering issues
- **Scanned PDF Support** - Extract text from image-only PDFs using OCR
- **Best Quality Available** - Uses superior online AI OCR when possible
- **Always Works** - Falls back to offline OCR when needed
- **Precise Selection** - Select exactly the text you need
- **Review Before Copy** - See and edit what you're copying
- **User-Friendly** - Simple click-and-drag interface with clear visual feedback
- **Hebrew-Optimized** - RTL text direction, Hebrew button labels, and Hebrew OCR
- **Privacy-Conscious** - Offline mode available for sensitive documents
- **No Setup Required** - Works immediately with demo API key

### Performance Notes

- **Text Layer Extraction**: Instant
- **Online OCR (OCR.space)**: 1-2 seconds (depends on internet speed)
- **Offline OCR (Tesseract)**: 2-4 seconds (depends on rectangle size and text complexity)
- **First Use**: Tesseract.js library loads from CDN (~500 KB)
- **Memory**: Tesseract worker stays in memory for faster subsequent OCR operations
- **API Limits**: Demo key is shared across all users, may be rate-limited during heavy usage

### Future Enhancements

- **User API Key Configuration**: Allow users to optionally configure their own OCR.space API key in settings for better rate limits (25,000 requests/month per user instead of shared demo key)
- **OCR Engine Selection**: Let users choose between online/offline OCR or disable online OCR entirely
- **Additional OCR Providers**: Support for Azure Computer Vision or Google Cloud Vision APIs for even better accuracy

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
- **rectangle-selection.js** - New file with rectangle selection implementation (OCR disabled)
- **locale/he/viewer.ftl** - Added Hebrew translations (`rectangle-selection-button` and `rectangle-selection-button-label`)
- **locale/en-US/viewer.ftl** - Added English translations (`rectangle-selection-button` and `rectangle-selection-button-label`)
- **Bug fixes**: Added null check in `onMouseUp` handler, fixed translation ID format

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

## Dev Mode PDF Loading

### Problem

In development mode (browser without C# WebView2), PDFs loaded via file picker need to work with blob URLs. The initial implementation had issues with blob URL accessibility and memory efficiency.

### Solution

Use blob URLs (created with `URL.createObjectURL()`) instead of data URLs for memory-efficient PDF loading in dev mode.

### Implementation

**Files Modified**:

- `src/components/pdf/PdfViewPage.vue` - File picker uses blob URLs
- `src/components/home/HomePage.vue` - File picker uses blob URLs

**Why Blob URLs**:

```javascript
// ✅ GOOD: Memory efficient
const fileUrl = URL.createObjectURL(file); // Blob URL

// ❌ BAD: Loads entire file into memory as base64
const reader = new FileReader();
reader.readAsDataURL(file); // Data URL
```

### How It Works

1. **File Selection** - User selects PDF file via browser file picker
2. **Blob URL Creation** - `URL.createObjectURL(file)` creates a blob URL (e.g., `blob:http://localhost:5173/uuid`)
3. **PDF.js Loading** - Blob URL is passed to PDF.js viewer iframe
4. **Same-Origin Access** - Blob URLs work across iframe boundaries when both are on same origin (localhost:5173)
5. **Memory Efficiency** - File stays in browser's blob storage, not duplicated in memory

### Benefits

- **Memory Efficient** - Large PDFs don't consume excessive memory
- **Fast Loading** - No base64 encoding/decoding overhead
- **Same-Origin Compatible** - Works across iframe boundaries
- **Standard Approach** - Uses browser's native blob storage system

### C# Mode vs Dev Mode

| Feature     | C# Mode (WebView2)          | Dev Mode (Browser)   |
| ----------- | --------------------------- | -------------------- |
| File Picker | Native Windows dialog       | Browser file input   |
| URL Type    | Virtual HTTPS URL           | Blob URL             |
| Persistence | Can recreate from file path | Lost on page reload  |
| Memory      | Managed by C#               | Browser blob storage |

### References

- [URL.createObjectURL()](https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL)
- [Blob URLs](https://developer.mozilla.org/en-US/docs/Web/API/Blob)

## PDF Page Filters

### Problem

PDF pages with white backgrounds can be harsh on the eyes, especially in dark themes or when using colored reading backgrounds. Users need the ability to apply color filters to PDF pages to match their theme preferences.

### Solution

Implemented a unified CSS variable-based filter system that automatically calculates appropriate filters based on theme colors, with support for both built-in and custom themes.

### Implementation

**Files Modified**:

- `public/pdfjs/web/viewer-custom.css` - Single CSS rule using CSS variable
- `zayit-vue/src/utils/themes.ts` - Filter calculation and CSS variable injection
- `zayit-vue/src/data/themes.json` - Added `pdfFilter` property to all built-in themes
- `zayit-vue/src/data/themeTypes.ts` - Added optional `pdfFilter` property to Theme interface

### How It Works

**1. CSS Variable System**

All PDF page filters use a single CSS rule:

```css
:root[data-pdf-filters="true"] .pdfViewer .canvasWrapper canvas {
  filter: var(--pdf-page-filter, none);
}
```

**2. Filter Calculation**

For themes without an explicit `pdfFilter` property, the system automatically calculates an appropriate filter based on theme colors:

```typescript
function calculatePdfFilter(theme: Theme): string {
  if (theme.isDark) {
    // Dark themes: Inversion + color tint based on accent color
    // Analyzes accent color hue and saturation
    // More saturated accents get stronger color tints
    return "invert(0.9) hue-rotate(180deg) sepia(...) saturate(...) brightness(0.8)";
  } else {
    // Light themes: Detect warm backgrounds or saturated accents
    // Apply sepia for warm tones, color tints for saturated accents
    return "sepia(...) hue-rotate(...) saturate(...)";
  }
}
```

**3. Theme Sync Integration**

The filter is set as a CSS variable when syncing PDF viewer themes:

```typescript
// In syncPdfViewerTheme()
if (theme?.pdfFilter) {
  // Use explicitly defined filter
  iframeRootStyle.setProperty("--pdf-page-filter", theme.pdfFilter);
} else if (theme) {
  // Auto-calculate filter based on theme colors
  const autoFilter = calculatePdfFilter(theme);
  iframeRootStyle.setProperty("--pdf-page-filter", autoFilter);
}
```

### Filter Calculation Logic

**Dark Themes:**

1. Always use inversion as base: `invert(0.9) hue-rotate(180deg)`
2. Analyze accent color's hue and saturation
3. If saturation > 0.3, add color tint matching the accent:
   - Extract hue angle (0-360°)
   - Calculate sepia amount based on saturation
   - Apply hue rotation to match accent color
   - Increase saturation for vibrant effect
4. Add brightness and contrast adjustments

**Light Themes:**

1. Check for warm backgrounds (yellowish/sepia tones):
   - If `r + g > 2 * b` and warmth > 0.2, apply sepia filter
2. Check for saturated accent colors:
   - If saturation > 0.4, apply color tint matching accent
3. Neutral themes get no filter (clean white)

### Built-in Theme Filters

All 36 built-in themes have pre-defined filters optimized for their color schemes:

**Light Themes:**

- Sepia/Warm: `sepia(1) brightness(0.9)`
- Green: `sepia(0.9) hue-rotate(60deg) saturate(1.5)`
- Blue: `sepia(0.7) hue-rotate(180deg) saturate(1.5)`
- Pink/Rose: `sepia(0.9) hue-rotate(300deg) saturate(1.5)`
- Gray: `saturate(0.2) brightness(0.94)`
- Fluent: `none` (clean white)

**Dark Themes:**

- All use inversion + color tints: `invert(0.9) hue-rotate(180deg) sepia(...) hue-rotate(...) saturate(...) brightness(0.8) contrast(0.9)`
- Color tints match the theme family (warm, green, blue, purple, etc.)

### Custom Theme Support

Custom themes automatically get calculated filters based on their colors:

1. **With `pdfFilter` property**: Uses the specified filter
2. **Without `pdfFilter` property**: Automatically calculates based on:
   - Background color (warm vs cool)
   - Accent color (hue and saturation)
   - Whether it's a dark or light theme

### User Control

Users can toggle PDF page filters on/off via settings:

```typescript
setPdfPageFilters(enabled: boolean);
```

This sets the `data-pdf-filters` attribute on the document root, which controls whether the CSS filter is applied.

### Benefits

- **Unified System** - Single CSS variable for all themes
- **Automatic Calculation** - Custom themes get appropriate filters without manual configuration
- **Theme Matching** - Filters adapt to theme colors for visual consistency
- **Performance** - CSS filters are GPU-accelerated
- **No Observers** - Clean CSS variable approach, no DOM mutation observers needed
- **Easy Customization** - Themes can override with explicit `pdfFilter` property
- **Instant Updates** - Filter changes apply immediately via CSS variables

### Technical Details

**Color Analysis:**

- Extracts RGB values from hex colors
- Calculates HSL (hue, saturation, lightness)
- Detects warm vs cool tones
- Measures color saturation for tint intensity

**Filter Composition:**

- Dark themes: `invert` → `hue-rotate` → `sepia` → `hue-rotate` → `saturate` → `brightness` → `contrast`
- Light themes: `sepia` → `hue-rotate` → `saturate` → `brightness`

**Performance:**

- Filters applied via CSS, GPU-accelerated
- No JavaScript processing during rendering
- Calculated once per theme change
- Minimal performance impact

### Example Filters

```typescript
// Sepia theme (light)
pdfFilter: "sepia(1) brightness(0.9)";

// Dracula theme (dark)
pdfFilter: "invert(0.9) hue-rotate(180deg) sepia(0.7) hue-rotate(260deg) saturate(1.6) brightness(0.8) contrast(0.9)";

// Custom warm theme (auto-calculated)
// Background: #f4ecd8, Accent: #d4a574
// Result: "sepia(0.85) brightness(0.89)"
```

### References

- [CSS filter property](https://developer.mozilla.org/en-US/docs/Web/CSS/filter)
- [CSS custom properties](https://developer.mozilla.org/en-US/docs/Web/CSS/--*)
- [HSL color model](https://en.wikipedia.org/wiki/HSL_and_HSV)


## Cross-Origin Virtual Host Access

### Problem

PDF.js validates that the `file=` URL passed to the viewer is same-origin as the viewer itself. When serving local PDF files via WebView2 `SetVirtualHostNameToFolderMapping`, each folder gets its own virtual hostname (e.g. `http://KitveiHakodesh-pdf-1/`), which is a different origin from the viewer host (`http://KitveiHakodesh-vue-app/`). PDF.js throws `file origin does not match viewer's` and shows a blank viewer.

### Solution

Patched `validateFileURL` in `viewer.mjs` to allow any `http://` origin through the check. Since PDF.js runs inside a trusted WebView2 host (not a public browser), there is no security concern with this.

### Implementation

**File**: `public/pdfjs/web/viewer.mjs` (inside `validateFileURL` function)

Added before the error throw:

```javascript
// Allow any http:// origin (WebView2 virtual hosts for local file serving)
if (fileOrigin && fileOrigin.startsWith("http://")) {
  return;
}
```

### How It Works

WebView2's `SetVirtualHostNameToFolderMapping` maps arbitrary hostnames to local folders. PDF files served this way have a different origin than the viewer. This patch allows any `http://` origin so any virtual host mapping works without restriction.

### Applicability

This patch is intentionally generic — it works for any app using WebView2 virtual host mappings, regardless of the hostname convention used.

## Mobile Toolbar Replacement

### Problem

The native PDF.js toolbar does not contract on small viewports — it overflows and gets cut off. It is not suitable for Android-sized screens.

### Solution

The native toolbar is hidden entirely via CSS. A Vue component (`PdfToolbar.vue`) renders a compact 44px toolbar above the iframe with all the same functionality. The findbar is moved out of the toolbar DOM so it survives the toolbar being hidden.

### Implementation

**viewer-custom.css** — hides `.toolbar` and `#toolbarContainer`, expands `#mainContainer` and `#viewerContainer` to `top: 0`, positions `#findbar` as a `position: fixed` overlay at the top when visible, suppresses the doorHanger triangle decorations on the findbar.

**viewer.html** — `#findbar` is moved out of `#toolbarViewerLeft` and placed as a direct child of `#mainContainer` immediately before `#viewerContainer`. The `#viewFindButton` remains in the toolbar (hidden with it) because PDF.js's `PDFFindBar` wires its toggle logic to that button — our Vue toolbar clicks it programmatically.

**PdfToolbar.vue** (`src/components/pdf/`) — Vue component rendered above the iframe. Controls: sidebar toggle (clicks `#viewsManagerToggleButton`), find toggle (clicks `#viewFindButton`), prev/next page, page number input, zoom out/select/in, and a "more" dropdown with download and rectangle selection. Communicates via `contentWindow.PDFViewerApplication`. Polls every 400ms to sync page number and zoom state.

### What still works

- Sidebar (TOC outline, thumbnails, attachments, layers) — fully intact, toggled by our toolbar
- Findbar — moved to `#mainContainer`, floats as a fixed overlay at the top when open
- All editor panels (highlight, freetext, ink, stamp) — untouched, PDF.js enables them per-document
- Theme sync — `syncPdfViewerTheme` in `themes.ts` is unaffected
- Rectangle selection tool — triggered from the "more" menu via `window.toggleRectangleSelection()`
- Download with save dialog — triggered via `PDFViewerApplication.downloadOrSave()`
