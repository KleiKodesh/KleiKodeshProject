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

### 0y. Preserve zoom on destination jumps (ignore baked-in `/Fit`)

Our Hebrew, ABBYY-produced PDFs bake a `/Fit` (fit-page) zoom into both their
OpenAction and their outline/named destinations. PDF.js honors that zoom in
`PDFViewer.scrollPageIntoView`, so the view snapped to **page-fit** on every
destination jump — on reload (OpenAction) and on TOC/outline clicks (named
dest). Scroll and thumbnail navigation were unaffected because they pass no
`destArray` and return early.

Fix: always ignore the destination-supplied zoom so a jump changes the page but
keeps the user's current scale. Only adopt a scale when none exists yet
(`UNKNOWN_SCALE`, i.e. the first paint), so the initial view still gets `"auto"`.

In `PDFViewer.scrollPageIntoView`, search for:
```js
    if (!ignoreDestinationZoom) {
      if (scale && scale !== this._currentScale) {
        this.currentScaleValue = scale;
      } else if (this._currentScale === UNKNOWN_SCALE) {
        this.currentScaleValue = DEFAULT_SCALE_VALUE;
      }
    }
```

Replace with:
```js
    // PATCH: preserve the current zoom on every destination jump. These Hebrew
    // (ABBYY-produced) PDFs bake a "/Fit" zoom into their OpenAction and outline
    // destinations, so PDF.js was snapping to page-fit on reload and on TOC/
    // outline clicks (scroll and thumbnail nav were unaffected — they carry no
    // destArray). Forcing ignoreDestinationZoom here jumps to the right page but
    // keeps whatever scale the user set. We only adopt a scale when none exists
    // yet (UNKNOWN_SCALE), so the very first paint still has a sane zoom.
    const ignoreZoom = true; // was: !ignoreDestinationZoom
    if (!ignoreZoom) {
      if (scale && scale !== this._currentScale) {
        this.currentScaleValue = scale;
      } else if (this._currentScale === UNKNOWN_SCALE) {
        this.currentScaleValue = DEFAULT_SCALE_VALUE;
      }
    } else if (this._currentScale === UNKNOWN_SCALE) {
      this.currentScaleValue = DEFAULT_SCALE_VALUE;
    }
```

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

### Outline (table of contents) label

The default Hebrew for the outline view title and its view-selector option is the
verbose "תוכן העניינים של המסמך". Shorten both to "תוכן עניינים" (keep the `.title`
double-click hint, just drop "של המסמך"):

```
pdfjs-views-manager-outlines-title1 = תוכן עניינים
    .title = הצגת תוכן העניינים (יש ללחוץ לחיצה כפולה כדי להרחיב או לצמצם את כל הפריטים)
pdfjs-views-manager-outlines-option-label = תוכן עניינים
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

## Pages panel — "select all" checkbox

Adds a tri-state **select-all checkbox** to the pages-manager (`viewsManager`) status
bar, so the user can select or deselect every page with one click instead of ticking
each thumbnail. It sits immediately before the existing status label
(`pdfjs-views-manager-pages-status-none-action-label` = "בחירת עמודים" / "N נבחרו"),
which acts as its caption. State mirrors the selection: **unchecked** (none),
**indeterminate** (some), **checked** (all). Clicking it selects all pages, or clears
them when all are already selected. This supersedes PDF.js's own tiny deselect
icon-button (`#viewsManagerStatusActionDeselectButton`), which is hidden in CSS.

Requires `enableSplitMerge: true` (see the feature-flags section) — the whole
pages-manager UI, per-page checkboxes, and status bar only exist when it is enabled.

### `web/viewer.html` — checkbox element

Inside `<span id="viewsManagerStatusActionLabelContainer">`, add the checkbox as the
**first child**, immediately before `<button id="viewsManagerStatusActionDeselectButton" …>`:

```html
<!-- CUSTOM: select-all checkbox — one click selects/deselects every page.
     Sits before the status label ("בחירת עמודים" / "N נבחרו"), which acts as its caption. -->
<input
  id="viewsManagerSelectAllCheckbox"
  type="checkbox"
  tabindex="0"
  title="בחר / בטל בחירת כל העמודים"
  aria-label="בחר / בטל בחירת כל העמודים"
/>
```

### `web/viewer.mjs` — wiring (4 edits)

**1. Register the element in `getViewerConfiguration()`.** In the `viewsManagerStatusBar`
object (alongside `viewsManagerStatusActionLabel`), add:

```js
viewsManagerStatusActionSelectAllCheckbox: document.getElementById("viewsManagerSelectAllCheckbox")
```

**2. In `class PDFThumbnailViewer`**, add a private field next to `#deselectButton = null;`:

```js
#selectAllCheckbox = null;
```

and, in the constructor next to `this.#deselectButton = statusBar?.viewsManagerStatusActionDeselectButton || null;`, add:

```js
this.#selectAllCheckbox = statusBar?.viewsManagerStatusActionSelectAllCheckbox || null;
```

**3. Wire the change listener** immediately after `this.#deselectButton.classList.toggle("hidden", true);`
(inside the `if (this.#enableSplitMerge && manageMenu) { … }` block):

```js
this.#selectAllCheckbox?.addEventListener("change", this.#toggleSelectAll.bind(this));
```

**4. Add three methods** immediately after `#selectPage(pageNumber, checked) { … }`:

```js
// CUSTOM: select-all checkbox in the pages panel status bar.
// Tri-state: unchecked (nothing selected) / indeterminate (some) / checked (all).
// Toggling it selects or deselects every page at once.
#toggleSelectAll() {
  if (!this.#enableSplitMerge || !this._thumbnails?.length) {
    return;
  }
  if (this.#selectAllCheckbox?.checked) {
    this.#selectAllPages();
  } else {
    this.#clearSelection();
  }
  this.#updateMenuEntries();
  this.#updateStatus("select");
}
#selectAllPages() {
  if (this.#hasUndoBarVisible) {
    this.#dismissUndo(false);
  }
  const set = this.#selectedPages ??= new Set();
  for (let i = 0, ii = this._thumbnails.length; i < ii; i++) {
    this._thumbnails[i].toggleSelected(true);
    set.add(i + 1);
  }
}
#updateSelectAllState() {
  const checkbox = this.#selectAllCheckbox;
  if (!checkbox) {
    return;
  }
  const total = this._thumbnails?.length || 0;
  const size = this.#selectedPages?.size || 0;
  checkbox.disabled = total === 0;
  checkbox.indeterminate = size > 0 && size < total;
  checkbox.checked = total > 0 && size >= total;
}
```

Then call `this.#updateSelectAllState();` from the two methods that run on every
selection change, so the checkbox stays in sync:

- at the end of `#updateMenuEntries()`;
- inside `#updateStatus(type)`, in the `if (type === "select") { … }` branch, immediately
  before its `return;`.

### `web/viewer-custom.css` — styling

```css
#viewsManagerSelectAllCheckbox {
  width: 16px;
  height: 16px;
  margin: 0;
  flex: 0 0 auto;
  cursor: pointer;
  accent-color: var(--accent-color-custom, #0a84ff);
}
#viewsManagerSelectAllCheckbox:disabled { cursor: default; opacity: 0.5; }
#viewsManagerSelectAllCheckbox:not(:disabled):hover {
  outline: 1px solid var(--accent-color-custom, #0a84ff);
  outline-offset: 1px;
}
/* Keep the status caption on one line — the checkbox now shares its row. */
#viewsManagerStatusActionLabel { white-space: nowrap; }
/* Hide PDF.js's built-in deselect icon-button — the checkbox supersedes it. */
#viewsManagerStatusActionDeselectButton { display: none !important; }
```

### No locale change needed

The checkbox's `title` / `aria-label` are hardcoded Hebrew (`בחר / בטל בחירת כל העמודים`)
and it has **no** `data-l10n-id`, so nothing needs adding to `web/locale/**/viewer.ftl`.
Its visible caption is the pre-existing, already-translated status label next to it.
This matches the app's Hebrew-first convention (the iframe is always loaded with
`?locale=he`) and keeps the patch self-contained.

Verified live in Chromium against the 14-page sample PDF: select-all checks every page
(status → `N נבחרו`, Export enabled); unchecking one page flips the checkbox to
indeterminate; clicking it again re-selects all; clicking when fully checked clears the
selection (status → `בחירת עמודים`).

---

## Pages panel — default to the outline when the document has one

By default the side panel always opens to the pages/thumbnail view (`SidebarView.THUMBS`).
This makes the **outline** (`תוכן עניינים`) the default view instead — but only when the
document actually has a table of contents, and only when neither the PDF's page mode nor a
stored preference explicitly asked for a specific view. If the document has no outline it
stays on pages; an explicit choice is always respected.

All edits are in `web/viewer.mjs`, in `class ViewsManager extends Sidebar`.

**1. Add a private field** immediately after `#hasAnimations = …;`:

```js
// CUSTOM: true when the PDF's page mode or a stored preference explicitly picked
// a sidebar view — used so the "default to outline" logic below never overrides
// a deliberate choice.
#initialViewWasExplicit = false;
```

**2. In `reset()`**, add after `this.isInitialEventDispatched = false;`:

```js
this.#initialViewWasExplicit = false;
```

**3. In `setInitialView(view = SidebarView.NONE)`**, record whether the view was explicit.
After `this.isInitialViewSet = true;` and before the `if (view === SidebarView.NONE …)` guard:

```js
// CUSTOM: record whether an explicit sidebar view was requested (vs. the -1
// "unknown" default) so a later-loaded outline can safely become the default.
this.#initialViewWasExplicit = view !== SidebarView.NONE && view !== SidebarView.UNKNOWN;
```

**4. In `#addEventListeners()`**, inside the `eventBus._on("outlineloaded", …)` handler,
immediately after the `onTreeLoaded(evt.outlineCount, this.outlineButton, SidebarView.OUTLINE);` line:

```js
// CUSTOM: when the document has a table of contents (תוכן עניינים) and no
// view was explicitly requested by the PDF/prefs, make the outline the
// default view instead of the pages thumbnails. If the sidebar is closed
// this simply sets the view shown the next time the user opens it.
if (evt.outlineCount > 0 && !this.#initialViewWasExplicit && this.active === SidebarView.THUMBS) {
  this.switchView(SidebarView.OUTLINE);
}
```

`outlineloaded` fires after the outline DOM is rendered and (in this app's flow) after
`setInitialView` runs, so the `#initialViewWasExplicit` flag is already set. When the sidebar
is closed (the common case — `sidebarViewOnLoad` defaults to `-1`), `switchView(OUTLINE)` only
updates `active`; the outline is shown the next time the user opens the panel. Even if the
event ever arrives before `setInitialView`, a later explicit view still wins.

Verified live in Chromium: a PDF with a 2-item outline defaults to the outline view
(`active === OUTLINE`, header label `תוכן עניינים`, outline shown on open); the no-outline
sample stays on the pages view.

---

## Pages panel — tighter header bars

PDF.js's default views-manager header wastes vertical space: the title bar uses 12px
top/bottom padding and the status bar has a 64px `min-height`. Trim both. In
`web/viewer-custom.css` (loads after `viewer.css`, so equal-specificity selectors win by
load order — the selectors below deliberately use 3 IDs, +1 descendant for `> div`, to match
PDF.js's own generated specificity):

```css
#viewsManager #viewsManagerHeader #viewsManagerTitle {
  padding-top: 5px;
  padding-bottom: 5px;
}
#viewsManager #viewsManagerHeader #viewsManagerStatus > div {
  min-height: 42px;
}
```

Live-measured result: title bar ~56px → 42px, status bar 64px → 42px.

---

## Pages panel — gap between the selection count and the Manage dropdown

The status bar lays out `#viewsManagerStatusActionLabelContainer` (the select-all checkbox +
`N נבחרו` count) and `#actionSelector` (the Manage dropdown) with `justify-content:
space-between`. But `#actionSelector` sizes to its **hidden popup menu** — whose longest item
is `ייצוא הפריטים שנבחרו…` ("Export selected…") — so it renders ~158px wide. In a ~270px
sidebar that leaves almost no room, and the count label ends up ~2px from the button.

Cap `#actionSelector` to its own button's content width so `space-between` restores a proper
gap; keep a 12px floor and never let the count label shrink. The popup menu is absolutely
positioned, so it still opens at its full width (verified). Add to `web/viewer-custom.css`
(selectors use 5 IDs to match/beat PDF.js's generated `#actionSelector` rule):

```css
#viewsManager #viewsManagerHeader #viewsManagerStatus #viewsManagerStatusAction {
  gap: 12px;
}
#viewsManager #viewsManagerHeader #viewsManagerStatus #viewsManagerStatusAction #viewsManagerStatusActionLabelContainer {
  flex: 0 0 auto;
}
#viewsManager #viewsManagerHeader #viewsManagerStatus #viewsManagerStatusAction #actionSelector {
  min-width: 0;
  max-width: 100px;
}
```

Live-measured result: Manage button 158px → 100px, gap between count and button 2px → ~41–48px;
`ניהול` not clipped; Manage menu still opens at full 158px.

---

## Hide the file-loading progress bar

The thin 4px bar under the toolbar (`#loadingBar`) that fills as a PDF loads is not wanted.
It is absolutely positioned, so hiding it has no layout effect, and PDF.js keeps updating it
internally without error — only the visual is removed. Add to `web/viewer-custom.css`:

```css
#loadingBar {
  display: none !important;
}
```

Verified live: `#loadingBar` computes to `display: none` (0×0) throughout load, the PDF still
loads normally, and there are no console errors.

---

## Outline panel — search input (תוכן עניינים), flat ranked results

Adds a search input to the outline side panel that behaves **identically to the BookView
TOC side-panel search**: typing filters the table of contents down to a flat, ranked list
of matches (best first); clearing the query restores the normal nested tree.

Entirely additive — one new file plus a `<script>` tag and a CSS block. **No `viewer.mjs`
patches**, so nothing here conflicts with a PDF.js upgrade; the only upgrade risk is the
outline DOM shape changing (see "If the outline DOM changes" below).

### Added file: `web/outline-search.js`

Self-contained. Two parts:

1. **A hand-written port of the Vue app's `SegmentSearchTree`**
   (`vue-frontend/src/utils/segmentSearchTree.ts`) — `tokenizeSegmentText` plus the
   3-pass scoring algorithm (score with segment-crossing penalty → bond detection →
   ancestry dedup), including the exact-last-word retry and the Talmud-page suffix rule.
2. **The panel wiring** — DOM indexer, flat result renderer, and event hookup.

**Why a port and not an import.** `public/` is copied to the output verbatim and is not
part of the Vue module graph; importing from `src/` would couple `public/pdfjs/` to the
Vue build output, which nothing else there does. The duplication is deliberate.

**Keeping the two copies in sync** is guarded by a test:
`vue-frontend/src/utils/outlineSearchPortEquivalence.test.ts` loads `outline-search.js`,
extracts the ported class, and asserts it produces byte-identical rankings to
`segmentSearchTree.ts` across 27 queries plus tokenizer, display-path, result-limit, and
leaf-only-candidate checks. **If you change either implementation, run
`npm run test` — a divergence fails there.** The test locates the port's internals by
searching for the literal `  if (document.readyState === ` line, so keep that line as the
IIFE's last statement.

### How the filtering works

- On `outlineloaded`, the index is invalidated (lazily rebuilt on first keystroke, so a
  document with a TOC the user never searches costs nothing).
- `indexOutline()` reads `#outlinesView`'s nested `div.treeItem > a` DOM into a flat
  `{id, parentId, text, anchor}` list. Nesting is
  `div.treeItem > div.treeItems > div.treeItem`, so an item's parent is its nearest
  ancestor `.treeItem` — this reconstructs the same ancestor chain that `parentId` gives
  the Vue tree, which is what makes segment-aware scoring possible.
- While a query is active, `#outlinesView` gets `.outlineSearchActive` (`display:none`)
  and a sibling `#outlineSearchResults` holds the flat rows. It reuses the `.treeView`
  class **without** `withNesting`, so rows are styled exactly like real outline rows but
  unindented.
- Each result row's anchor is a **clone** (href + bold/italic styles copied). Clicking it
  is delegated to the original anchor via `.click()`, so PDF.js's own `_bindLink` handler
  stays the single source of truth for navigation and selected-item tracking. Nothing in
  `viewer.mjs` needed changing.
- Results are capped at **100**, matching the Vue `TreeView`'s filtered-list limit.
- Each row shows its ancestor path as a `.outlineSearchPath` subtitle, mirroring the Vue
  panel's result rows.

The input is shown only when the outline view is active **and** `outlineCount > 0`.
`Escape` clears the query (and stops propagating so it does not close the sidebar instead).

**Keyboard navigation** works in **both modes** — the flat results while a query is
active, and the nested outline tree otherwise — and is driven entirely from the search
input's `keydown`, so there is one code path and the caret never has to leave the box.
It is a port of the Vue app's `useListKeys` (`src/composables/useListKeyNav.ts`), the same
composable that drives the BookView TOC list: **Up/Down** move, **Home/End** jump to the
ends, **Enter** activates.

In the **tree**, two extra bindings handle hierarchy. Because the outline is RTL,
**ArrowLeft expands** (goes deeper) and **ArrowRight collapses** (goes back toward the
root), mirroring the chevron direction in BookView's RTL tree; collapsing an
already-collapsed row climbs to its parent. Both rely on PDF.js internals verified in
`viewer.mjs` / `viewer.css`:

- `.treeItemToggler` is `prepend`ed as the **first child** of `.treeItem`
  (`_addToggleButton`), hence the `:scope > .treeItemToggler` lookup.
- `.treeItemsHidden ~ .treeItems { display: none }` is the collapsed state, so collapsed
  rows are genuinely `display:none` and `navItems()` filters them with
  `offsetParent === null`. Without that filter, arrowing would step through invisible rows.

Focus is **not** carried across a mode switch: the tree and the results have unrelated
orderings, so an index would land the ring on an arbitrary row. `paintFocus()` clears
rings in *both* containers before painting, so switching modes or collapsing the focused
row can never strand a stale highlight.

**Space** toggles expand/collapse on the focused tree row, and opens it when it is a leaf
— mirroring BookView's `TreeNode.vue`, where Space toggles a parent and selects a leaf.
It is bound **only while the query box is empty**: queries are multi-word
(`"פסחים דף ד"`), so Space has to stay typable the moment there is any text. This is the
one place the port cannot follow BookView unconditionally — there the list is not a text
field, here it is.

One deliberate difference from `useListKeys`, because the key target here is a text input
rather than a list: focus is **painted** on the row (`.is-focused`) rather than moved there
as real DOM focus, so the caret stays in the query box and the user can keep typing to
refine while arrowing. `scrollIntoView({ block: 'nearest' })` matches `useListKeys`.

**Arrowing starts from the current entry, not from the top.** The first Up/Down press in
the tree seeds the ring onto the row for the page being viewed (`seedFocusFromCurrent()`)
and consumes that keypress, so the next press moves off it. Arrowing is a continuation of
"where am I" rather than a fresh traversal — in a 500-page book, starting at row 0 would
mean hundreds of presses to get back to the reading position. The seed calls
`revealCurrentEntry()` first, since the current row may sit inside a collapsed branch and
must be expanded before `navItems()` will consider it navigable. If there is no current
entry yet, it falls through to row 0 as before. Results mode is excluded — there the top
hit is already the right starting point.

Re-rendering on each keystroke resets the index to the **top hit**, so Enter alone opens
the best match. Activation calls `.click()` on the row, which flows through the same
delegated handler as a real mouse click (a synthetic click bubbles and `closest()` matches
it), so there is exactly one navigation path.

**Focus must be reclaimed after every activation**, or keyboard navigation dies after a
single Enter. **Two** separate things steal it, at two different times:

1. **The row itself** — these are real `<a href>` elements, so activating one (by Enter or
   by mouse, in either mode) focuses it synchronously.
2. **`PDFLinkService.goToDestination()`** — it registers a one-shot `textlayerrendered`
   listener and calls `evt.source.textLayer.div.focus()` when the destination page
   renders (`viewer.mjs`, in `goToDestination`). That is **asynchronous and unbounded**:
   on a cold page it can land hundreds of ms later, long after any `requestAnimationFrame`
   we might wait on. The text layer is focusable because `TextLayerBuilder` sets
   `this.div.tabIndex = 0`.

A single deferred re-focus therefore is not enough — that was a real bug. `refocusInput()`
instead focuses the input immediately and opens a **1 s reclaim window**; a capturing
document-level `focusin` listener (it bubbles, unlike `focus`) pulls focus back for the
duration. Reclaiming is restricted to the elements activation is *known* to focus — an
outline anchor or a `.textLayer` — so it never yanks focus from somewhere the user
deliberately clicked, and the window is short enough that it cannot fight a later genuine
focus change.

Mouse clicks additionally sync the ring to the clicked row, so a following Enter/Arrow
continues from there.

The tree's click listener is bound on `#outlinesView`, the **same element** PDF.js binds
its toggler listener to — `stopEvent()`'s `stopPropagation()` does not prevent other
listeners on that same element from running, so both coexist. Toggler clicks are skipped
explicitly so expanding/collapsing does not move the ring.

**Autofocus** is driven from `updateVisibility()` on the hidden→visible transition, *not*
from any single event. Visibility depends on two inputs — the active view and
`outlineCount` — and either can be the last to arrive: the "default to the outline"
customization above switches the view **before** the outline loads, so
`sidebarviewchanged` fires while `outlineCount` is still 0 (bar correctly hidden, no focus)
and it is `outlineloaded` that reveals the bar. Driving focus off the transition covers
both orderings. Two details make it robust:

- A `wasVisible` flag limits focus to the transition itself. PDF.js's `open()` calls
  `switchView(this.active)` and then `#dispatchEvent()` unconditionally, so opening the
  sidebar dispatches `sidebarviewchanged` twice with the same view; the flag also stops a
  later unrelated dispatch from yanking focus out of the outline list while the user is
  arrowing through it.
- The focus call is deferred one `requestAnimationFrame`, because the bar is revealed by
  removing `.hidden` in the same tick and `focus()` does nothing on a `display:none`
  element.

Re-focusing on every open (not just the first) falls out of `#dispatchEvent()` sending
`view: this.visibleView`, whose getter returns `SidebarView.NONE` while the sidebar is
closed — so closing resets `wasVisible` and reopening is a genuine transition again.

### `web/viewer.html` — script tag

Add immediately after `<script src="viewer.mjs" type="module"></script>`:

```html
<!-- CUSTOM: outline (תוכן עניינים) search input — flat ranked results,
     mirroring the BookView TOC side-panel search. Self-contained; it polls for
     PDFViewerApplication, so load order relative to viewer.mjs does not matter. -->
<script src="outline-search.js" defer></script>
```

### `web/viewer-custom.css`

The search bar is inserted **after** `#viewsManagerContent` (below the scroll area, like
BookView's `.toc-search`, which renders after its tree). Two things about the parent
`#viewsManager` matter, and both cost a rule:

- It is `display:flex; flex-direction:column; **align-items:flex-start**` — so
  `align-self: stretch` is **required**, otherwise the bar shrinks to its content width.
- It has `**padding-bottom: 16px**`. That was invisible while the scrollable
  `#viewsManagerContent` was the last child, but the search bar is now last, so the
  padding renders as a stray gap between the input and the panel's bottom edge. The
  `:has()` rule below zeroes it only while the bar is visible, so the pages/attachments/
  layers views keep PDF.js's original 16px. (`:has()` takes the specificity of its most
  specific argument, so `#viewsManager:has(#outlineSearchBar…)` is 2 IDs and beats
  PDF.js's 1-ID `#viewsManager` rule regardless of load order.)

```css
#outlinesView.outlineSearchActive { display: none; }

#viewsManager:has(#outlineSearchBar:not(.hidden)) { padding-bottom: 0; }

#outlineSearchBar {
  flex: 0 0 auto;
  align-self: stretch;   /* required — parent is align-items:flex-start */
  /* no margin/padding-bottom here — the parent's 16px is zeroed above instead */
  box-sizing: border-box;
  padding: 5px 6px 6px;
  border-top: 1px solid var(--separator-color);
  background: var(--toolbar-bg-color);
}
#outlineSearchBar.hidden { display: none; }
#outlineSearchInner { display: flex; align-items: center; padding: 1px 6px; }
#outlineSearchInput {
  flex: 1; width: 0; min-width: 0;
  padding: 0;
  height: 18px; line-height: 18px;   /* see note below */
  direction: rtl; text-align: right;  /* see the bidi note below */
  background: none; border: none; outline: none;
  font-size: 12px; font-family: inherit; color: var(--main-color);
}
.outlineSearchPath {
  display: block;
  direction: rtl; unicode-bidi: isolate;
  font-size: 11px; line-height: 13px; opacity: 0.6;
}

/* keyboard focus ring — both containers */
#outlineSearchResults .treeItem > a.is-focused,
#outlinesView .treeItem > a.is-focused {
  background-color: color-mix(in srgb, var(--main-color) 10%, transparent) !important;
  background-clip: padding-box;
  border-radius: 2px;
}
/* suppress PDF.js's .selected styling inside the results — see note below */
#outlineSearchResults .treeItem.selected > a {
  background-color: transparent;
  color: var(--treeitem-color);
}
#outlineSearchInput::placeholder { color: var(--main-color); opacity: 0.6; }
#outlineSearchInput::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }
.outlineSearchPath { display: block; font-size: 11px; line-height: 13px; opacity: 0.6; }
```

Colors use PDF.js's own themed variables (which `viewer-custom.css` already maps onto the
app's `--*-custom` values).

**Bidi — every new text surface needs `direction: rtl`.** The viewer root is `dir="ltr"`,
so any Hebrew string ending in weak punctuation (`:` or `.` — extremely common in these
TOCs, e.g. `דף ד:`) renders with that punctuation jumping to the *wrong end*. The
pre-existing "Hebrew RTL outline (TOC) sidebar" rule above fixes this for
`.treeItem > a`, which the flat result rows inherit for free because they are built with
the same `div.treeItem > a` shape — that is a reason to keep that shape. But the rule does
**not** reach the two surfaces this feature adds, so both set it themselves:

- `#outlineSearchInput` — without it a typed query like `ד:` shows the colon on the left.
  `text-align: right` keeps the caret on the Hebrew side.
- `.outlineSearchPath` — uses `unicode-bidi: isolate` (not `embed`) because the path joins
  ancestor titles with `" · "`, and an ancestor ending in `:` would otherwise reorder
  against that separator.

If you add another text element here, set `direction: rtl` on it too.

**Two highlight gotchas**, both worth knowing before retuning these colors:

- The focus ring must **not** use `--treeitem-selected-bg-color` (0.25 alpha). That is
  PDF.js's *selected* weight and reads far too dark for a transient ring. The
  `color-mix(… 10%)` above matches the Vue app's `[data-nav-item].is-focused` and stays
  lighter than the 0.15 hover token, so hovering a focused row still reads as a change.
- Opening a search result routes through the original anchor's handler, so PDF.js's
  `_updateCurrentTreeItem` marks that row `.selected` — and since `#outlineSearchResults`
  also carries the `.treeView` class, PDF.js's `.selected:is(.treeView .treeItem) > a`
  rule painted the matching **results** row with that same heavy color, which lingered
  after the query was cleared. The suppression rule above scopes that styling back to the
  real tree, where it belongs (it is the current-entry indicator — see the
  current-entry-tracking section).

**Mangled titles are a separate, non-CSS problem.** ABBYY FineReader and some other
producers store Hebrew outline titles with trailing punctuation encoded in visual/LTR
order, so the string literally begins with the punctuation: `".מח"` instead of `"מח."`.
No amount of `direction: rtl` fixes that — the characters really are in that order.
`normalizeOutlineTitle()` in `outline-search.js` detects leading punctuation followed by a
Hebrew letter and moves it to the end. It runs in two places:

- `normalizeOutlineDom()`, on `outlineloaded`, rewrites PDF.js's own rendered tree in
  place (`_finishRendering()` appends the DOM *before* dispatching the event, so it is
  available). Correcting the DOM rather than patching `PDFOutlineViewer.render` keeps this
  additive and upgrade-safe.
- `indexOutline()`, so search matches what the user sees — a query for `מח.` finds a title
  stored as `.מח`.

This is a **port of the Vue app's `normalizeOutlineTitle`**
(`src/features/pdf-viewer/usePdfViewPageTracking.ts`), which applies the same correction to
the titlebar breadcrumb; that copy predates the sidebar search and only ever covered the
breadcrumb. Keep the two in sync — including the regex's `.` (not `[\s\S]`), which leaves
titles containing a newline untouched instead of reordering across the line break.

**On the compact sizing.** The paddings are deliberately tighter than
`BookViewTocTree.vue`'s `.toc-search` (which uses `5px 6px 6px` + `4px 8px`): the PDF
sidebar is narrower and shorter than the BookView panel, so the same values read as a
too-tall row there. Three paddings stack in this bar — the bar's, `#outlineSearchInner`'s,
and the input's own — plus the UA's default `height`/`line-height` on `input[type=search]`,
which is **not** derived from `font-size` and silently adds a few px. `padding: 0` and an
explicit `height`/`line-height` on the input pin the row to exactly the 12px text plus the
two paddings above it (~24px total, down from ~37px). If you retune this, change the
paddings and leave the height pins alone, or the UA default creeps back in.

### No locale change needed

The placeholder (`חיפוש...`) and tooltip are hardcoded Hebrew with no `data-l10n-id`,
matching the select-all checkbox convention above — the iframe is always loaded with
`?locale=he`.

### Current-entry tracking (follows the page as you scroll)

The outline row covering the page being viewed becomes PDF.js's **selected** item, updated
on every `pagechanging` and on document load — the same relationship BookView's line view
has with its TOC. Every ancestor is expanded recursively so the row is visible, and it is
scrolled into view with `block: 'nearest'`.

`revealCurrentEntry()` calls **`pdfOutlineViewer._updateCurrentTreeItem()`** rather than
maintaining a parallel highlight class. That is PDF.js's own selected-item bookkeeping: it
holds `_currentTreeItem` and clears the previous row when a new one is set, so page
tracking and a manual click share one highlight and one piece of state and can never leave
two rows marked. There is a defensive fallback that toggles `.selected` directly if that
private method is ever renamed upstream.

The ancestor walk mirrors PDF.js's `_scrollToCurrentTreeItem`, with one deliberate
difference: it scrolls `block: 'nearest'` where PDF.js uses `'center'`, which would yank
the list on every page turn.

**PDF.js has this feature and it cannot be reused here.** `_currentOutlineItem()` does
exactly this, but it is gated on `_isPagesLoaded`, and `_dispatchEvent()` resolves its
`currentOutlineItemPromise` to `false` whenever `disableAutoFetch` is set — which this
viewer sets deliberately (see the memory-reduction AppOptions section). So its toolbar
button is permanently disabled here. The underlying reason is `_getPageNumberToDestHash()`
resolving pages via `cachedPageNumber()`, which only answers once every page is loaded.

`buildPageIndex()` instead resolves each destination with **`getPageIndex()`**, which works
without loading all pages — the same approach the Vue app's `usePdfViewPageTracking` uses
for the titlebar breadcrumb. Rows are matched to entries by **destination hash**
(`linkService.getDestinationHash(dest)`), which is exactly what PDF.js assigns to each
anchor's `href` in `_bindLink`, so the two align by construction; this avoids assuming the
BFS render order matches `querySelectorAll` document order (they differ for nested trees).
The active entry for page N is the **last** entry with `page <= N`, matching BookView's
`getActiveTocEntry`.

A `pageIndexToken` counter guards the async build: switching documents bumps it so an
in-flight build from the previous document discards its result. Revealing is suppressed
while the panel is hidden or search results are showing, and re-triggered when the panel
opens or the query is cleared.

No custom CSS is involved — the current row is styled by PDF.js's existing
`.selected:is(.treeView .treeItem) > a` rule. The keyboard focus ring (`.is-focused`) is
separate and can sit on a different row, which is correct: one means "where the page is",
the other "where the keyboard is".

### Outline tree restyled to match BookView's TOC

The panel is a copy of BookView's TOC system, matched against **measurements of the
live BookView panel** (`capture-both-tocs.mjs` captured both panels under the real
theme and dumped computed styles + geometry) — not against a reading of its source:

- rows span the **full panel width** (hover/press/current are full-bleed strips), 28px,
  line-height 28px, 0.8rem, **no radius**, Segoe UI stack
- chevron: a 24×28 flex-centred column at the row's inline-start (right), shifted
  **depth × 10px**; text starts at `24 + depth*10` from the right — the exact numbers
  measured from `TreeNode.vue` rows (`chevFromRight: 0/10…`, `textFromRight: 24/34…`)
- hover 6% / press 10% / current 8% text-color mixes; current label = accent, weight 500
- one Fluent `ChevronDown16Regular` mask that **rotates 90°** when collapsed
- the search box is BookView's **pill**: 999px radius, 10% text-secondary fill, 1px
  border, `padding: 4px 8px`, on the `.toc-search`-style bar

**The structural trick — indent INSIDE full-width rows.** PDF.js indents by nesting
`.treeItems` containers, so a row's box starts at its indent and a highlight can never
span the panel (the old "floating grey box" look). The container margins are zeroed and
`outline-search.js` stamps `--toc-depth` on every `.treeItem` (`stampDepths()`, one O(N)
pass on every tree build/edit); the anchor pads by `depth*10px + 24px` and the toggler
sits absolutely in that gutter. `inset-inline-start` resolves to the RIGHT because
PDF.js sets `dir=rtl` on the document at runtime under `locale=he`.

Colors come from the `--*-custom` tokens `themes.ts` injects. The DOM is untouched —
editor, search, keyboard nav, tracking, and DnD are unaffected (verified 26/26 + 18/18
after the change; 4,400-entry perf unchanged).

Upgrade traps: the margin-zeroing and collapsed-chevron rules need the `#outlinesView`
id to beat viewer.css (the collapsed rule must also re-declare `mask-image`, or the
stock icon swap survives as a `scaleX` flip). Testing trap: the chevron transition
(120ms) means reading its computed `transform` in the same tick as the class change
returns the START value — assert the matrix (`matrix(0,1,-1,0)`) after waiting it out.

### Outline EDITING (add / rename / delete / move / re-nest, and create-from-scratch)

**Direct manipulation, no edit mode** — the interaction model every PDF editor
(Acrobat, PDF Expert, PDF-XChange, Calibre's Edit-ToC) converged on. Rows always
navigate on click; editing happens on the rows themselves:

- **`+` button** in the search row (`#outlineAddButton`, Ctrl+B): adds an entry
  pointing at the CURRENT page — after the ring row, else after the current-entry row,
  else at the end — and drops straight into rename with the name pre-selected. Pressed
  while search results are showing, it clears the query first so the insertion is
  visible.
- **Right-click context menu** (`#outlineContextMenu`) on any row — the action hub:
  שינוי שם (F2) / עדכון יעד לעמוד הנוכחי / הוספת פריט אחרי / הוספת תת־פריט / מחיקה
  (Del). Also reachable via the hover `⋯` button (`#outlineRowMenuButton` — a single
  floating element repositioned onto the hovered row, so the PDF.js-owned tree DOM
  stays untouched and per-row cost is zero).
- **Drag & drop** (pointer-based, 5px threshold so plain clicks still navigate): drop
  zones per hovered row are top quarter = before, bottom quarter = after, middle =
  INTO (last child, prospective parent highlighted). A fixed-position insertion line
  (`#outlineDropIndicator`) tracks the sibling positions; drops into/beside the dragged
  row's own subtree are rejected; the container auto-scrolls near its edges. After a
  real drop, the click that follows pointerup is swallowed via a capture listener
  removed on a 0-timeout — NOT `{once:true}`, which would linger if no click ever
  fires (pointer released outside the window) and eat the next unrelated click.
- **Double-click renames** (the first click of the pair navigates — harmless); Escape
  cancels, Enter/blur commits, and only an ACTUAL text change dirties the document.
- **Keyboard** (from the search input, ring row as target): F2 rename, Delete remove
  (empty query only — the key must stay typable), Ctrl+B add, **Alt+↑/↓ move among
  siblings, Alt+←/→ indent/outdent** (RTL: Alt+← nests deeper, matching plain ←'s
  expand direction).
- **עדכון יעד לעמוד הנוכחי** (retarget) re-points a row at the current page: it
  becomes editor-owned — `data-toc-page` set, `src` stamp and href/onclick dropped —
  and navigates like an added row.

There are deliberately NO visual unsaved-changes cues in the panel (no dirty dot, no
markers on added/retargeted rows) — the dirty STATE still drives the save flow, the
beforeunload alert and the host-side close guards; it just is not painted.

Edits are written into the **PDF itself** on save via the standard save pipeline — NOT
stored app-side. Verified live end-to-end: edit → save → reload the saved bytes →
outline persists (including on a doc with no outline at all).

**The DOM is the model.** The rendered tree is already the source of truth for search,
tracking, and keyboard nav, so editing it directly keeps all of those working with no
parallel structure. Rows keep PDF.js's exact shape (`.treeItem > a`, `.treeItems`,
`.treeItemToggler`), so styling and collapse come for free. Rows with editor-owned
targets (added or retargeted) carry `data-toc-new` (dashed start-edge marker) and **no
href** (PDF.js only binds navigation to anchors it created; an empty href would push
history entries). Delete **promotes children** rather than dropping the subtree.
XFA documents: every interaction trigger checks `editingDisabled()` (the worker's
outline block skips isPureXfa, so edits would silently discard on save).

**Page stamps.** `buildPageIndex` was refactored to stamp each row with
`data-toc-page` instead of building a detached index; `rebuildPageIndexFromDom()`
re-derives the sorted index from those stamps. This is what lets edits participate in
current-entry tracking immediately, and it is where an added row's target page lives
(the page being viewed at add time). `serializeOutlineDom()` walks the tree to
`[{title, page, items}]` — titles are the DISPLAYED text, so saving persists the
mangled-title correction permanently. Rows whose destination never resolved inherit the
nearest previous row's page rather than being dropped.

**Save path — 4 patched files, re-apply all on upgrade:**

1. **`web/outline-search.js`** (added file, no re-apply needed) —
   `pushOutlineToTransport()` sets `pdfDocument._transport.editedOutline` after every
   edit; null means "no edits" and save behaves exactly as stock.

2. **`build/pdf.mjs`** — in `WorkerTransport.saveDocument()`, add
   `editedOutline: this.editedOutline ?? null` to the `SaveDocument` payload (search for
   `sendWithPromise("SaveDocument"`).

3. **`build/pdf.worker.mjs`** — two edits. Add `editedOutline` to the `SaveDocument`
   handler's destructured params. Then, immediately after
   `const refs = await Promise.all(promises);`, insert the outline-writing block (search
   for "PATCH: write a user-edited outline"): it builds outline-item `Dict`s with
   Title (`stringToAsciiOrUTF16BE`) / Parent / Prev / Next / First / Last / Count, puts
   them into `changes`, and puts a **copied catalog** (same ref, `/Outlines` re-pointed
   or omitted) alongside — the trailer's Root ref is unchanged, so the incremental
   update stays valid. The `changes.put(ref, {data: dict})` form is serialized by
   `writeChanges` → `writeObject`, the same path annotations use — encryption
   transforms included.
   **Placement is critical: the block must run BEFORE the `changes.size === 0` early
   return**, or an outline-only save returns the original bytes unchanged.

   **Attribute preservation (`src`).** Entries carry `src` — their index into the
   ORIGINAL outline flattened in the same BFS order the viewer uses — and the worker
   fetches `pdfManager.ensureCatalog("documentOutlineForEditor")` (whose items keep
   `rawDict`) to copy everything the editor does not model **verbatim from the raw
   dict**: `/Dest` (precise XYZ, named destinations) or `/A` (URL and other actions),
   `/F` (bold/italic), `/C` (color), and the closed-branch state (negative `/Count`,
   recomputed over the possibly-changed children but keeping the original sign). This
   is a SAME-document save, so raw values including indirect refs are valid as-is — no
   cross-document cloning like `PDFEditor.#setOutlineItemDest` needs. Without `src`
   (user-created rows) the entry gets a `[pageRef /XYZ null null null]` Dest; a
   malformed page ref leaves the entry title-only rather than writing `Dest [null …]`.
   Verified live: renaming one entry + adding one preserves, byte-exact in semantics,
   a precise XYZ dest with coordinates/zoom, a named destination (still resolving
   through `/Dests`), a URL action, bold, italic+color, a `/Fit` dest, a closed branch
   (`count:-2`), and a nested child's coordinates.

   **Catalog seeding.** The block seeds its catalog copy from
   `changes.get(catalogRef)?.data` when present — `StructTreeRoot.createStructureTree`
   puts an updated catalog into `changes` when saving annotations creates a structure
   tree on an untagged PDF, and seeding from the original would clobber its
   `/StructTreeRoot` and orphan the whole structure tree.

4. **`web/viewer.mjs`** — in `downloadOrSave()`, the save/download decision checks only
   `annotationStorage.size > 0`; add `|| this.pdfDocument?._transport?.editedOutline != null`
   (search for "PATCH: also route through save()"). Without this an outline-only edit
   takes the `download()` branch and silently writes the ORIGINAL bytes.

**Dirty state.** `outlineDirty` is OR-ed into the viewer's dirty logic by wrapping
`app._hasChanges` at runtime (no viewer.mjs patch; the `beforeunload` handler is bound to
the app object, so the wrapper is what it calls). The + button shows a dot while dirty.
Verified live: the beforeunload alert fires for outline-only edits. Everything resets on
`documentloaded`.

**Create-from-scratch.** A PDF with no outline: PDF.js disables the תוכן עניינים option
and bounces the sidebar off the outline view (`onTreeLoaded` → `switchView(THUMBS)`).
The panel re-enables the option in its own `outlineloaded` handler (registered later, so
it runs after the disable), and `updateVisibility` shows the bar whenever a document is
loaded — even with zero entries — so the + button is reachable and the first added entry
starts the outline. The worker patch handles the no-existing-outline case natively: it
builds a new `/Outlines` root and points the copied catalog at it. (`PDFEditor`'s own
`#makeOutline` was NOT reused — it builds a whole new document; the patch reuses only its
dict-shape conventions.)

**Readiness signal.** `#outlineSearchBar[data-outline-loaded="1"]` is set once outline
processing (including the empty-outline dance) has settled, and removed on
`documentloaded`. PDF.js re-enables all view buttons early during document setup, so
button state alone is a racy readiness probe — tests and the host app should key on this
attribute instead.

### Host (Vue app) integration — unsaved-edits guard

The viewer's iframe is destroyed or navigated by the host on tab switches, so unsaved
edits are pushed OUT eagerly — after every edit, not at teardown (there is no reliable
teardown moment). Contract, all on the iframe `window`:

- **`__khOutlineHostNotify({dirty, outline})`** — assigned by the host
  (`usePdfViewPageTracking.attach`); called after every edit and, with `dirty:false`,
  after a COMPLETED save. The host parks the snapshot per tab
  (`bookViewStore.pdfEditStateByTabId`) and its close guards read it for background tabs
  whose viewer no longer exists.
- **`__khOutlineEditor`** — `{getState, setState, isDirty}`. The host calls
  `setState({outline})` to rehydrate parked edits when a tab returns with the same file
  (rows are rebuilt with `data-toc-page` and no href — navigation goes through the
  `goToPage` fallback, and the next save writes exactly this tree).
- **`__khSuppressUnloadPrompt`** — set by the host just before a Vue-initiated
  navigation/teardown. The `_hasChanges` wrapper returns false while set, silencing
  PDF.js's own beforeunload prompt for navigations whose state the host already holds.
  Without it, switching between two PDF tabs pops the browser's native English prompt
  mid-switch — and cancelling that desyncs the iframe from the already-switched tab.
- **`kh-save-complete`** (DOM event on `document`) — dispatched by the patched
  `_triggerDownload` in `viewer.mjs` only after the file was actually written (or the
  fallback anchor download fired). A cancelled save picker dispatches nothing, so edits
  correctly stay dirty. Clears the editor's dirty flag and notifies the host.

Vue side (not in this folder): `bookViewStore` (snapshots + `hasUnsavedPdfChanges` +
dialog state), `tabStore` (synchronous close guards at the single chokepoint all close
paths funnel through, including the native chrome-tabs mirror), `App.vue` (themed
ConfirmDialog + window `beforeunload`). **Known gap:** the WinForms WebView2 host closing
its window does not run `beforeunload` — the C# side needs a FormClosing hook that asks
the Vue app (`bookViewStore.hasAnyUnsavedPdfChanges()`) before allowing close.

**Known limitations (deliberate):**

- **Page-organizer combo:** `downloadOrSave` takes the `hasStructuralChanges()` →
  `onSavePages` branch first, and that pipeline (`PDFEditor.extractPages`) rebuilds the
  outline itself from the original — sidebar outline edits do not ride it, and the
  `data-toc-page` stamps would be stale after reordering anyway. Combining TOC edits
  with page reorganization in one session is unsupported; save one before doing the
  other.
- **`close()` auto-save not extended:** PDF.js's `close()` auto-saves only when
  `_annotationStorageModified`. Outline edits are deliberately NOT wired into it — in
  this app `close()` runs during host-driven teardown, where popping a save dialog is
  exactly what the Vue-side snapshot/guard system exists to prevent.
- **Per-document reset rides `documentinit`, not `documentloaded`:** the latter only
  fires after `getDownloadInfo()` resolves — i.e. after the FULL file downloads — which
  under this viewer's `disableAutoFetch: true` is late or never for an in-place
  `open()`. `documentinit` fires right after `setInitialView`, per document, regardless
  of download progress.

### If the outline DOM changes on upgrade

Only two assumptions matter, both in `indexOutline()`:
`.treeItem` is the per-item wrapper class, and an item's own anchor is `:scope > a`.
Verify those in `PDFOutlineViewer.render()` (search for `div.className = "treeItem"`).
The editor additionally assumes top-level `.treeItem`s are DIRECT children of
`#outlinesView` (`serializeOutlineDom`'s root walk and `addItem`'s fallback append),
and that `.treeItemToggler` is prepended as the row's first child.

---

## Vue App Integration (no changes needed on upgrade)

These live in the Vue app and do not need to be re-applied to PDF.js:

- `PdfViewPage.vue` — passes `?file=`, `?locale=he`, `?filename=`, `?cMapPacked=true` to the iframe src
- `themes.ts syncPdfViewerTheme()` — injects `--*-custom` CSS variables and `--pdf-filter-custom` into the iframe
- `settingsStore.togglePdfPageFilters()` — sets `data-pdf-filters` attribute on the iframe document element
