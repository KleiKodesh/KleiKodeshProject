# PDF.js Customizations

Base version: **5.7.284**
Applied to: `vue-frontend/public/pdfjs/`

When upgrading to a new PDF.js release, re-apply every item in this file.
Search for the surrounding code shown under each patch — line numbers shift between releases but the surrounding code is stable.

---

## Added Files

These files do not exist in the vanilla PDF.js dist and must be created fresh each upgrade.

### `web/pixel-ratio-override.js`

Forces a minimum `devicePixelRatio` for sharp PDF rendering on low-DPI displays.
Must be loaded **before** `viewer.mjs`.

Why 1.5× and not 2×: each canvas uses `width × height × devicePixelRatio²` bytes. At 2× every canvas is 4× larger than at 1×; at 1.5× canvases are 2.25× larger — still noticeably sharper than 1×, but using only 56% of the memory that 2× would require. On displays already at ≥1.5× (125%+ Windows scaling, retina) this script is a no-op.

**Old-Chromium blur fix (floor rises to 2.0 when CSS `round()` is missing).** PDF.js's `setLayerDimensions` (in `pdf.mjs`) snaps each page's CSS box down to a whole multiple of device pixels via CSS `round()`, so the integer-pixel canvas maps 1:1 to the screen and stays crisp:

```js
const useRound = FeatureTest.isCSSRoundSupported;   // CSS.supports("width: round(1.5px, 1px)")
const widthStr = useRound ? `round(down, ${w}, var(--scale-round-x))` : `calc(${w})`;
```

CSS `round()` only shipped in Chromium 111. On older builds `useRound` is false and the page box becomes a fractional `calc()` size — the canvas is then scaled by a non-integer factor and pages look **slightly blurry**. `round()` can't be polyfilled live (the box is recomputed by CSS on every zoom), so instead the override raises the pixel-ratio floor to 2.0 on those builds; the browser downscales the higher-resolution canvas (supersampling), which hides the sub-pixel misalignment. The support test uses the **same expression PDF.js uses**, so the bump kicks in precisely when PDF.js takes the blurry `calc()` path. Modern Chromium keeps the leaner 1.5×.

```js
(function () {
  const original = window.devicePixelRatio || 1;

  let cssRoundSupported = false;
  try {
    cssRoundSupported = !!(window.CSS && CSS.supports && CSS.supports("width: round(1.5px, 1px)"));
  } catch (e) {
    cssRoundSupported = false;
  }

  const floor = cssRoundSupported ? 1.5 : 2.0;
  const enhanced = Math.max(original, floor);

  if (enhanced !== original) {
    try {
      Object.defineProperty(window, 'devicePixelRatio', {
        get: function () { return enhanced; },
        configurable: true,
      });
    } catch (e) {
      // Some engines may refuse to redefine devicePixelRatio; keep the native value.
    }
  }
})();
```

### `web/viewer-custom.css`

Theme variable hooks and PDF page filter support. The Vue app's `syncPdfViewerTheme()` injects `--*-custom` CSS variables into the iframe; this file maps them onto PDF.js's own CSS variables so the viewer adopts the app's theme automatically.

The page filter (`--pdf-filter-custom`) is applied only when the `data-pdf-filters="true"` attribute is set on the iframe's `<html>` element, which `settingsStore.togglePdfPageFilters()` controls.

```css
:root {
  --toolbar-bg-color: var(--bg-primary-custom, light-dark(rgb(249 249 250), rgb(56 56 61)));
  --toolbar-border-color: var(--border-color-custom, light-dark(rgb(184 184 184), rgb(92 92 97)));
  --main-color: var(--text-primary-custom, light-dark(rgb(12 12 13), rgb(249 249 250)));
  --body-bg-color: var(--bg-secondary-custom, light-dark(rgb(212 212 215), rgb(35 35 39)));
  --progressBar-color: var(--accent-color-custom, #0a84ff);
  --doorhanger-bg-color: var(--bg-primary-custom, light-dark(rgb(255 255 255), rgb(56 56 61)));
  --doorhanger-border-color: var(--border-color-custom, light-dark(rgb(184 184 184), rgb(92 92 97)));
  --button-hover-color: var(--hover-bg-custom, light-dark(rgba(0 0 0 / 0.08), rgba(255 255 255 / 0.08)));
  --toggled-btn-bg-color: var(--active-bg-custom, light-dark(rgba(0 0 0 / 0.12), rgba(255 255 255 / 0.12)));
  --field-bg-color: var(--bg-primary-custom, light-dark(rgb(255 255 255), rgb(56 56 61)));
  --field-border-color: var(--border-color-custom, light-dark(rgb(187 187 188), rgb(115 115 115)));
  --separator-color: var(--border-color-custom, light-dark(rgba(0 0 0 / 0.08), rgba(255 255 255 / 0.08)));
}

:root[data-pdf-filters="true"] #viewerContainer .page canvas {
  filter: var(--pdf-filter-custom, none);
}

:root {
  --scrollbar-color: var(--border-color-custom, auto);
  --scrollbar-bg-color: transparent;
}
```

---

## `build/pdf.worker.mjs` — Patches

### Map.prototype.getOrInsertComputed polyfill (old WebView2 compatibility)

`Map.prototype.getOrInsertComputed` was added in Chromium 136. PDF.js uses it heavily throughout `pdf.worker.mjs`, `pdf.mjs`, and `viewer.mjs` — for `intentStates`, `methodPromises`, `_cachedBitmapsMap`, and many more internal maps. Users on WebView2 builds older than Chromium 136 get `"TypeError: this[methodPromises].getOrInsertComputed is not a function"` and no PDF loads at all. A secondary symptom is `"ReferenceError: Cannot access '_firstPagePromise' before initialization"` — that is a consequence of the first crash, not an independent bug.

The polyfill is needed in **two places**:

1. **`build/pdf.worker.mjs`** — covers the Worker thread (`pdf.worker.mjs` runs in a Web Worker, not the main window). Insert immediately after the `Uint8Array.prototype.toHex` polyfill block.

2. **`web/viewer.html`** — covers the main thread (`pdf.mjs` and `viewer.mjs` run in the page window). Add as an inline `<script>` block immediately before `<script src="viewer.mjs" type="module">`.

The polyfill itself:
```js
if (typeof Map.prototype.getOrInsertComputed !== 'function') {
  Map.prototype.getOrInsertComputed = function (key, callbackFn) {
    if (!this.has(key)) {
      this.set(key, callbackFn(key));
    }
    return this.get(key);
  };
}
```

If a future PDF.js version drops the `.getOrInsertComputed()` calls (because Chromium 136 is no longer a supported baseline), this patch can be dropped from both files.

---

### Uint8Array.prototype.toHex polyfill (old WebView2 compatibility)

`Uint8Array.prototype.toHex` was added in Chromium 136. PDF.js calls `.toHex()` on `Uint8Array` values in the `fingerprints` getter (converting MD5 bytes and PDF trailer ID bytes to hex strings). Users on WebView2 builds older than Chromium 136 get `"UnknownErrorException: hashOriginal.toHex is not a function"` and no PDF loads at all.

Insert the polyfill block immediately after the version/build comment at the top of the file (the `pdfjsVersion` / `pdfjsBuild` block). Search for the end of that comment followed by the webpack runtime comment — the exact build hash will differ on upgrade, but the pattern is stable:

```js
 * pdfjsBuild = <hash>
 */
/******/ // The require scope
```

Replace with:

```js
 * pdfjsBuild = <hash>
 */

// PATCH: Uint8Array.prototype.toHex polyfill for WebView2 builds on Chromium < 136.
// PDF.js calls .toHex() on Uint8Array values returned by calculateMD5() and on raw
// bytes from the PDF trailer ID array. Chromium 136 introduced this method natively;
// older WebView2 builds don't have it and throw
// "UnknownErrorException: hashOriginal.toHex is not a function" when loading any PDF.
if (typeof Uint8Array.prototype.toHex !== 'function') {
  Uint8Array.prototype.toHex = function () {
    return Array.from(this, (byte) => byte.toString(16).padStart(2, '0')).join('');
  };
}

/******/ // The require scope
```

If a future PDF.js version removes the `.toHex()` call entirely (because Chromium 136 is no longer a supported baseline), this patch can be dropped.

---

## `web/viewer.html` — Added Tags

Add these two lines immediately after `<link rel="stylesheet" href="viewer.css" />`:

```html
<link rel="stylesheet" href="viewer-custom.css" />
<script src="pixel-ratio-override.js"></script>
```

Then add this inline polyfill script immediately before `<script src="viewer.mjs" type="module">`:

```html
<script>
  // PATCH: Map.prototype.getOrInsertComputed polyfill for Chromium < 136.
  // pdf.mjs and viewer.mjs use this Map method extensively on the main thread.
  // Without it, PDF loading fails entirely on older WebView2 / Chrome builds.
  if (typeof Map.prototype.getOrInsertComputed !== 'function') {
    Map.prototype.getOrInsertComputed = function (key, callbackFn) {
      if (!this.has(key)) {
        this.set(key, callbackFn(key));
      }
      return this.get(key);
    };
  }
</script>
```

The `pixel-ratio-override.js` script tag must appear **before** `<script src="viewer.mjs" type="module">`.

---

## `web/viewer.mjs` — Patches

### Spread modes in presentation mode

By default PDF.js forces `SpreadMode.NONE` when entering presentation mode and only restores the previous spread if pages had unequal sizes. This blocks the user from changing spread modes while in presentation mode.

Three changes remove this restriction:

**1. `request()` — always save spreadMode in `#args`**

Search for:
```
    this.#args = {
      pageNumber: pdfViewer.currentPageNumber,
      scaleValue: pdfViewer.currentScaleValue,
      scrollMode: pdfViewer.scrollMode,
      spreadMode: null,
      annotationEditorMode: null
    };
    if (pdfViewer.spreadMode !== SpreadMode.NONE && !(pdfViewer.pageViewsReady && pdfViewer.hasEqualPageSizes)) {
      console.warn("Ignoring Spread modes when entering PresentationMode, " + "since the document may contain varying page sizes.");
      this.#args.spreadMode = pdfViewer.spreadMode;
    }
```
Replace with:
```
    this.#args = {
      pageNumber: pdfViewer.currentPageNumber,
      scaleValue: pdfViewer.currentScaleValue,
      scrollMode: pdfViewer.scrollMode,
      // PATCH: always save spreadMode so it can be restored on exit, regardless of page sizes
      spreadMode: pdfViewer.spreadMode,
      annotationEditorMode: null
    };
    if (pdfViewer.spreadMode !== SpreadMode.NONE && !(pdfViewer.pageViewsReady && pdfViewer.hasEqualPageSizes)) {
      // PATCH: removed the hard reset to SpreadMode.NONE — we now allow spread modes in presentation mode.
      console.warn("Spread modes are active in PresentationMode. " + "Document may contain varying page sizes — layout may be imperfect.");
    }
```

**2. `#enter()` — remove the forced reset to `SpreadMode.NONE`**

Search for:
```
      this.pdfViewer.scrollMode = ScrollMode.PAGE;
      if (this.#args.spreadMode !== null) {
        this.pdfViewer.spreadMode = SpreadMode.NONE;
      }
      this.pdfViewer.currentPageNumber = this.#args.pageNumber;
```
Replace with:
```
      this.pdfViewer.scrollMode = ScrollMode.PAGE;
      // PATCH: removed forced SpreadMode.NONE — spread modes are now allowed in presentation mode
      this.pdfViewer.currentPageNumber = this.#args.pageNumber;
```

**3. `#exit()` — always restore spreadMode (it is now always saved)**

Search for:
```
      this.pdfViewer.scrollMode = this.#args.scrollMode;
      if (this.#args.spreadMode !== null) {
        this.pdfViewer.spreadMode = this.#args.spreadMode;
      }
      this.pdfViewer.currentScaleValue = this.#args.scaleValue;
```
Replace with:
```
      this.pdfViewer.scrollMode = this.#args.scrollMode;
      // PATCH: always restore spreadMode (it is now always saved in #args)
      this.pdfViewer.spreadMode = this.#args.spreadMode;
      this.pdfViewer.currentScaleValue = this.#args.scaleValue;
```

---

### Presentation mode zoom (Ctrl+scroll, pinch, keyboard)

By default PDF.js blocks all zoom in presentation mode. These four changes re-enable it.

**1. `#mouseWheel` in `PDFPresentationMode`** — the presentation mode wheel listener calls `evt.preventDefault()` on every wheel event, consuming it before the main `onWheel` handler can see it. Add an early return for `Ctrl`/`Meta` so those events are not consumed here and fall through to the zoom handler.

Search for:
```
  #mouseWheel(evt) {
    if (!this.active) {
      return;
    }
    evt.preventDefault();
    const delta = normalizeWheelEventDelta(evt);
```
Replace with:
```
  #mouseWheel(evt) {
    if (!this.active) {
      return;
    }
    // PATCH: allow Ctrl+wheel to pass through for zoom — do not intercept it here.
    if (evt.ctrlKey || evt.metaKey) {
      return;
    }
    evt.preventDefault();
    const delta = normalizeWheelEventDelta(evt);
```

**2. `onWheel` function** — remove the early return that skips all wheel handling when in presentation mode. Search for (inside `function onWheel`, before `const deltaMode`):
```
  if (pdfViewer.isInPresentationMode) {
    return;
  }
  const deltaMode = evt.deltaMode;
```
Replace with:
```
  // PATCH: removed early return for isInPresentationMode — allow Ctrl+scroll zoom in presentation mode
  const deltaMode = evt.deltaMode;
```

**3. `updateZoom`** — remove the guard that blocks all zoom calls when in presentation mode:
```
  updateZoom(steps, scaleFactor, origin) {
    if (this.pdfViewer.isInPresentationMode) {
      return;
    }
    this.pdfViewer.updateScale({
```
Replace with:
```
  updateZoom(steps, scaleFactor, origin) {
    // PATCH: removed isInPresentationMode guard — allow zoom in presentation mode
    this.pdfViewer.updateScale({
```

**4. `isPinchingDisabled`** — remove the presentation mode check that disables pinch-to-zoom:
```
      isPinchingDisabled: () => pdfViewer.isInPresentationMode,
```
Replace with:
```
      isPinchingDisabled: () => false, // PATCH: allow pinch zoom in presentation mode
```

Keyboard zoom (`Ctrl++`/`Ctrl+-`) needs no change — those keycodes call `zoomIn()`/`zoomOut()` without a presentation mode guard; once `updateZoom` is unblocked they work automatically.

---

### 0z. Zoom step

Search for: `const DEFAULT_SCALE_DELTA = 1.1;`

Replace with:
```js
const DEFAULT_SCALE_DELTA = 1.02; // Custom: reduced from 1.1 (10%) to 1.02 (2%) per zoom step
```

---

## `web/viewer.html` — Added zoom input overlay

### Zoom input

Inside the existing `<span id="scaleSelectContainer" class="dropdownToolbarButton">`, add a sibling `<input id="zoomInput" type="text" readonly>` immediately after the `</select>` closing tag. No wrapper changes needed — the select stays exactly as-is.

The input is positioned absolutely over the text area of the select (covering the select's own rendered text), leaving the right 38px free so the select's native `::after` dropdown arrow remains fully visible and clickable. The select continues to work normally — clicking the arrow opens the dropdown as before.

Behavior:
- **Resting:** `type="text"` readonly, shows exactly the text of the currently selected option (mirrors to the letter, including named options like "אוטומטי", "רוחב העמוד", and numeric values like "125%"). Updated by a `MutationObserver` on the select subtree (catches Fluent async text updates) plus `scalechanging` and `change` events.
- **On focus:** switches to `type="number"`, shows the current scale as a plain integer percentage, selects all text.
- **On Enter:** applies `PDFViewerApplication.pdfViewer.currentScaleValue = (value / 100).toString()`, blurs.
- **On Escape or invalid input:** discards, reverts to resting state.
- **On blur:** applies zoom, reverts to resting state.
- Polls `waitForApp` until `PDFViewerApplication.pdfViewer` is ready and Fluent has translated the options before showing the first label.

The init script is added as a `<script>` block immediately before `</body>`.

---

## `web/viewer.html` + `viewer-custom.css` — Narrow-viewport zoom control

In a narrow toolbar the full zoom dropdown (`#scaleSelectContainer`, the select + the
`#zoomInput` overlay) is too wide. PDF.js's own responsive CSS simply hides it
(`@media (max-width: 560px){ #scaleSelectContainer{ display:none } }`), leaving no way
to change zoom. This customization instead **collapses it into a compact icon button**
at the same breakpoint, so tapping the icon opens the native option list directly (no
custom popover — the real `<select>` does the work, so the list keeps its original size
and theme).

### Added file: `web/images/toolbarButton-zoom.svg`

A "fit-to-width" glyph (a page rectangle with a horizontal double-headed arrow), used
as the collapsed button's icon. It is drawn to match PDF.js's own toolbar icon set —
**not** Fluent: PDF.js uses a custom set (solid `fill`/thin strokes, 16×16 viewBox).

### `viewer-custom.css`

At `@media (max-width: 560px)`, restyle `#scaleSelectContainer` into a 28px icon button
and swap the dropdown caret (`::after`) for the zoom glyph. Key points:

- Must set `display: inline-flex` to **override PDF.js's built-in `display:none`** at
  this exact breakpoint (viewer-custom.css loads after viewer.css, so it wins) — this is
  what makes the icon appear the instant the full dropdown would otherwise vanish.
- The real `<select>` stays **in flow** (so the button keeps its height — absolutely
  positioning it collapsed the container to 0px) and keeps its natural width so the
  native list opens at the original size; it is clipped by the 28px container's
  `overflow: hidden` and hidden with `opacity: 0` (still fully clickable).
- `#zoomInput` is `display:none` at the same breakpoint.

```css
@media (max-width: 560px) {
  #scaleSelectContainer {
    display: inline-flex;   /* override PDF.js's display:none at 560px */
    align-items: center;
    position: relative;
    min-width: 0;
    width: 28px;
    max-width: 28px;
    padding: 0;
    background: none;
    border: none;
    overflow: hidden;
  }
  #scaleSelectContainer:hover { background: var(--button-hover-color); }
  #scaleSelectContainer > select {
    flex: 0 0 auto;
    width: 140px;           /* natural width → native list opens at original size */
    opacity: 0;             /* invisible but clickable */
    cursor: pointer;
  }
  #scaleSelectContainer::after {
    inset-block: 0;
    inset-inline: 0;
    margin: auto;
    -webkit-mask-image: url(images/toolbarButton-zoom.svg);
            mask-image: url(images/toolbarButton-zoom.svg);
  }
}
@media (max-width: 560px) { #zoomInput { display: none; } }
```

### `viewer.html` — zoom input script

Since the collapsed button is icon-only, the zoom-input `mirrorSelect()` (see the zoom
input overlay section above) also writes the current zoom % into the container's tooltip:

```js
if (container && text) container.title = 'שינוי מרחק (זום) — ' + text;
```

---

## `web/locale/he/viewer.ftl` — Hebrew translation overrides

### Automatic zoom label

The default Hebrew translation for `pdfjs-page-scale-auto` is the verbose "מרחק מתצוגה אוטומטי". Change it to the concise:

```
pdfjs-page-scale-auto = אוטומטי
```

---

### 0a. Partial render delay (jump performance)

Search for:
```js
  minDurationToUpdateCanvas: {
    value: 500,
    kind: OptionKind.VIEWER
  },
```

Replace with:
```js
  minDurationToUpdateCanvas: {
    // PATCH: reduced from 500 to 0 so partial renders appear immediately on
    // large page jumps (e.g. page 5 → 350) rather than waiting 500ms before
    // showing any content. The page renders progressively as tiles complete.
    value: 0,
    kind: OptionKind.VIEWER
  },
```

PDF.js uses `enableOptimizedPartialRendering` to render pages in tiles. `minDurationToUpdateCanvas` is the minimum time that must pass before a partial tile update is shown to the user. At 500ms, a cold jump to a distant page shows nothing for half a second even though tiles are already rendered. Setting it to 0 makes content appear as soon as the first tile is ready.

---

### 0c. Reset renderingState after cancellation (stuck spinner fix)

This patch fixes pages getting permanently stuck on the loading animation after a large scroll jump.

**Root cause:** `_cancelRendering()` calls `pageView.cancelRendering()` on each page, which cancels the active `renderTask` but does **not** touch `renderingState`. Inside `_drawCanvas`, a cancelled task throws `RenderingCancelledException`, which causes an early return — the `this.renderingState = RenderingStates.FINISHED` line at the bottom of `_drawCanvas` is never reached. The page remains in `RUNNING` state indefinitely, spinner visible. The only accidental fix was zooming, because zoom calls `reset()` internally which explicitly sets the state back to `INITIAL`.

**The fix:** in `_cancelRendering()`, after calling `pageView.cancelRendering()`, check if the page was in `RUNNING` or `PAUSED` state and reset it to `INITIAL`. This removes the spinner immediately and allows the page to be re-queued for rendering correctly.

Search for:
```js
  _cancelRendering() {
    for (const pageView of this._pages) {
      pageView.cancelRendering();
    }
  }
```

Replace with:
```js
  _cancelRendering() {
    for (const pageView of this._pages) {
      // PATCH: reset renderingState to INITIAL after cancelling so that pages
      // stuck in RUNNING state (spinner visible) are cleaned up immediately.
      // Without this, cancelled pages stay in RUNNING state and the loading
      // spinner is never removed — the only way out was to zoom, which calls
      // reset() internally. Calling cancelRendering() only cancels the task;
      // it does not touch renderingState, so the spinner stays forever.
      const wasRunning = pageView.renderingState === RenderingStates.RUNNING ||
                         pageView.renderingState === RenderingStates.PAUSED;
      pageView.cancelRendering();
      if (wasRunning) {
        pageView.renderingState = RenderingStates.INITIAL;
      }
    }
  }
```

---

### 0b. Cancel in-progress renders on large scroll jumps

This is the most impactful patch for the "slow scrollbar jump" complaint.

**Root cause:** when the user drags the scrollbar far (page 10 → page 350), PDF.js sets the new page as highest priority but does **not cancel** the previously-rendering pages. Those pages hold live `renderTask` objects that continue executing on the microtask queue — their `onContinue` callbacks fire every tile, check `isHighestPriority`, pause themselves, then re-fire. This microtask churn competes with the new target page's first tiles, causing a blank-then-render delay that scales with how many pages were mid-render when the jump happened.

**The fix:** detect a large scroll jump in `_scrollUpdate` (jump distance > one viewport height in a single rAF tick, which only happens on scrollbar drag, not smooth scrolling), and call `_cancelRendering()` immediately before starting the new render cycle. This hard-cancels all in-progress `renderTask` objects so the new target page gets the full microtask queue and renders its first tiles as fast as possible.

Search for the `_scrollUpdate` method in the `PDFViewer` class:
```js
  _scrollUpdate() {
    if (this.pagesCount === 0) {
      return;
    }
    if (this.#scrollTimeoutId) {
      clearTimeout(this.#scrollTimeoutId);
    }
    this.#scrollTimeoutId = setTimeout(() => {
      this.#scrollTimeoutId = null;
      this.update();
    }, 100);
    this.update();
  }
```

Replace with (also add `#lastScrollTop = 0;` as a class field immediately before `_scrollUpdate`):
```js
  // PATCH: track the last known scrollTop to detect large jumps.
  #lastScrollTop = 0;
  _scrollUpdate() {
    if (this.pagesCount === 0) {
      return;
    }
    // PATCH: when the user drags the scrollbar to a distant page, the scroll
    // position jumps by more than one viewport height in a single rAF tick.
    // In that case, cancel all in-progress renders immediately so the new
    // target page gets the full CPU budget rather than competing with renders
    // for pages that are no longer visible.
    const currentScrollTop = this.container.scrollTop;
    const jumpThreshold = this.container.clientHeight;
    if (Math.abs(currentScrollTop - this.#lastScrollTop) > jumpThreshold) {
      this._cancelRendering();
    }
    this.#lastScrollTop = currentScrollTop;
    if (this.#scrollTimeoutId) {
      clearTimeout(this.#scrollTimeoutId);
    }
    this.#scrollTimeoutId = setTimeout(() => {
      this.#scrollTimeoutId = null;
      this.update();
    }, 100);
    this.update();
  }
```

---

### 0a. Page cache size (memory)

Search for: `const DEFAULT_CACHE_SIZE = 10;`

Replace with:
```js
// Reduced from 10 to 3 (current page + 1 on each side) to cut page-cache
// memory by ~70%. Each cached page holds a rendered canvas bitmap; at 1.5x
// devicePixelRatio a typical A4 page costs ~10 MB, so 10 pages = ~100 MB.
// 3 pages is sufficient for smooth scrolling in a read-only book reader.
const DEFAULT_CACHE_SIZE = 3;
```

PDF.js dynamically grows the cache to `max(DEFAULT_CACHE_SIZE, 2 * visiblePages + 1)` as the user scrolls, but the floor is this constant. Reducing it from 10 to 3 cuts the minimum page-cache footprint by ~70% with no visible impact on scrolling.

---

### 0. Canvas cleanup timeout (memory)

Search for: `const CLEANUP_TIMEOUT = 30000;`

Replace with:
```js
const CLEANUP_TIMEOUT = 5000; // Custom: reduced from 30000ms to 5000ms — frees canvas memory sooner when the user stops scrolling
```

PDF.js waits this many milliseconds of idle time before calling `cleanup()` on off-screen pages, which releases their canvas memory. 30 seconds is too long for a book reader where users frequently switch tabs or close PDFs. 5 seconds releases memory much sooner without any visible impact on scrolling performance.

---

### 0b. Memory-reduction AppOptions overrides

Search for: `function webViewerLoad() {`

Add the following block immediately after `const config = getViewerConfiguration();` and before the `const event = new CustomEvent(...)` line:

```js
// Custom: override AppOptions before the viewer initialises.
AppOptions.setAll({
  disablePreferences: true,     // prevent stored browser prefs from overwriting these settings
  enableScripting: false,       // no embedded JS in Hebrew books
  enableDetailCanvas: false,    // no second high-res canvas overlay
  // annotationMode and annotationEditorMode left at defaults so the full
  // annotation editor (highlight, freetext, signature, etc.) is available
  enableAutoLinking: false,     // no URL scanning in text layer
  maxCanvasPixels: 4096 * 4096, // cap canvas at ~16M px, not 33M
  disableAutoFetch: true,       // don't fetch the whole PDF upfront; MUST be set here, not in the URL hash
  disableStream: true,          // use direct range reads instead of chunked streaming; faster for local files via WebView2 virtual hosts
});
```

These options cannot be set via URL params — they are only settable via `AppOptions.setAll()` from inside the viewer's JS context. Setting them here, before `PDFViewerApplication.run()`, ensures they take effect before any page is rendered.

**Critical — why `disableAutoFetch` must not be in the URL hash:** PDF.js reads `document.location.hash.substring(1)` at startup and stores it verbatim as `initialBookmark`. Any hash value — even one containing only option flags like `disableAutoFetch=true` — is treated as a navigation destination and takes priority over the stored scroll/zoom position from `ViewHistory`. Putting `#disableAutoFetch=true` in the iframe URL therefore breaks session restore: the stored page/zoom is never applied because `initialBookmark` is always truthy. Setting it via `AppOptions.setAll()` here avoids this entirely. The iframe URL must have no hash fragment.

---

### 1. Hebrew locale default

Search for: `lang: navigator.language || "en-US"`

Replace with:
```js
lang: new URLSearchParams(window.location.search).get("locale") || "he"
```

The Vue app passes `?locale=he` in the iframe src. This reads it and falls back to Hebrew if absent.

---

### 2. Cross-origin allow for WebView2 virtual hosts

Search for the `validateFileURL` function. Find this block:
```js
const fileOrigin = URL.parse(file, window.location)?.origin;
if (fileOrigin === viewerOrigin) {
  return;
}
```

Add immediately after:
```js
// Allow WebView2 virtual hosts (http:// origins for local file serving)
if (fileOrigin && fileOrigin.startsWith("http://")) {
  return;
}
```

Without this, PDF.js rejects files served from WebView2 virtual hostnames like `http://KitveiHakodesh-pdf-1/`.

---

### 3. Filename URL parameter

Search for: `validateFileURL(file);`

Add immediately after:
```js
// Custom: read filename param for document properties and save dialog
const customFilename = params.get("filename");
if (customFilename) {
  this._contentDispositionFilename = decodeURIComponent(customFilename);
}
```

The Vue app passes `?filename=encodedName` so the original filename appears in document properties and the save dialog.

---

### 4. Save dialog with File System Access API

Find the `DownloadManager` class and its `_triggerDownload` method. Replace the entire method with:

```js
_triggerDownload(blobUrl, originalUrl, filename, isAttachment = false) {
  // Custom: use File System Access API save dialog when available
  if (blobUrl && !isAttachment && window.showSaveFilePicker) {
    (async () => {
      try {
        const response = await fetch(blobUrl);
        const blob = await response.blob();
        const handle = await window.showSaveFilePicker({
          suggestedName: filename || "document.pdf",
          types: [{ description: "PDF Files", accept: { "application/pdf": [".pdf"] } }],
        });
        const writable = await handle.createWritable();
        await writable.write(blob);
        await writable.close();
        if (blobUrl.startsWith("blob:")) URL.revokeObjectURL(blobUrl);
        return;
      } catch {
        // User cancelled or API error — fall through to default anchor download
      }
    })();
    return;
  }
  this._defaultTriggerDownload(blobUrl, originalUrl, filename, isAttachment);
}
_defaultTriggerDownload(blobUrl, originalUrl, filename, isAttachment = false) {
  if (!blobUrl && !isAttachment) {
    if (!createValidAbsoluteUrl(originalUrl, "http://example.com")) {
      throw new Error(`_triggerDownload - not a valid URL: ${originalUrl}`);
    }
    blobUrl = originalUrl + "#pdfjs.action=download";
  }
  const a = document.createElement("a");
  a.href = blobUrl;
  a.target = "_parent";
  if ("download" in a) {
    a.download = filename;
  }
  (document.body || document.documentElement).append(a);
  a.click();
  a.remove();
}
```

Shows a native OS save dialog (Chrome/Edge/WebView2). Falls back to automatic download if the user cancels or the API is unavailable.

---

### 5. Feature flags — set to `true`

Search for each option name in `defaultOptions` and change `value: false` to `value: true`:

| Option | Why |
|---|---|
| `enableSplitMerge` | Page reorganization UI (select, copy, cut, delete, reorder pages) |
| `enableMerge` | PDF merge UI |
| `enableComment` | Comment/annotation sidebar |
| `enableHighlightFloatingButton` | Floating highlight button when text is selected |
| `enableSignatureEditor` | Signature editor tool |
| `enableUpdatedAddImage` | Updated image insertion UI |
| `enableNewBadge` | "NEW" badge on new features |
| `enableOptimizedPartialRendering` | Performance: optimized partial page rendering |

These three remain `false` intentionally:
- `enableAltText` — triggers a ~50MB AI model download, irrelevant for a book reader
- `enablePermissions` — would disable editing on publisher-protected PDFs
- `pdfBugEnabled` — developer debugging tool only

---

## `web/viewer.html` — Page count floating badge

### Element

Add a `<div id="pageCountBadge"></div>` immediately before `</body>`.

### Init script

Add this `<script>` block immediately after the page count badge `<div>` (before `</body>`). It listens to the PDF.js internal `documentloaded` and `pagechanging` events via `PDFViewerApplication.eventBus` and updates the badge text to `"currentPage / totalPages"`. The badge starts empty and is only shown by CSS when it has content.

```js
(function () {
  function initPageCountBadge() {
    var badge = document.getElementById('pageCountBadge');
    if (!badge) return;

    function updateBadge(currentPage, totalPages) {
      if (!totalPages) { badge.textContent = ''; return; }
      badge.textContent = currentPage + ' / ' + totalPages;
    }

    function waitForApp() {
      var app = window.PDFViewerApplication;
      if (!app || !app.eventBus) { setTimeout(waitForApp, 100); return; }
      app.eventBus._on('documentloaded', function () { updateBadge(app.page, app.pagesCount); });
      app.eventBus._on('pagechanging', function (data) { updateBadge(data.pageNumber, app.pagesCount); });
      if (app.pdfDocument) { updateBadge(app.page, app.pagesCount); }
    }
    waitForApp();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initPageCountBadge);
  } else {
    initPageCountBadge();
  }
})();
```

### CSS (`viewer-custom.css`)

The badge is hidden by default and shown only when the toolbar is in compact mode (≤580px — same breakpoint where `#numPages` is hidden), and only when it has content. It fades in on page change and fades out after 2 seconds via a `setTimeout` that removes the `.visible` class. Deliberately unthemed (semi-transparent black/white) so it sits lightly on top of any PDF content.

```css
#pageCountBadge {
  display: none;
  position: fixed;
  bottom: 8px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1000;
  padding: 1px 5px;
  background: light-dark(rgba(255, 255, 255, 0.65), rgba(30, 30, 30, 0.65));
  color: light-dark(rgba(0, 0, 0, 0.6), rgba(255, 255, 255, 0.6));
  backdrop-filter: blur(4px);
  border-radius: 2px;
  font-size: 9px;
  line-height: 1.4;
  pointer-events: none;
  white-space: nowrap;
  opacity: 0;
  transition: opacity 300ms ease;
}

#pageCountBadge.visible { opacity: 1; }

@media (max-width: 580px) {
  #pageCountBadge:not(:empty) { display: block; }
}
```

---

## Vue App Integration (no changes needed on upgrade)

These live in the Vue app and do not need to be re-applied to PDF.js:

- `PdfViewPage.vue` — passes `?file=`, `?locale=he`, `?filename=`, `?cMapPacked=true` to the iframe src
- `themes.ts syncPdfViewerTheme()` — injects `--*-custom` CSS variables and `--pdf-filter-custom` into the iframe
- `settingsStore.togglePdfPageFilters()` — sets `data-pdf-filters` attribute on the iframe document element
