// Force a minimum pixel ratio for sharp PDF rendering on low-DPI displays.
// Must be loaded before PDF.js initializes.
//
// Why 1.5x and not 2x:
// Each PDF page canvas uses (width × height × devicePixelRatio²) bytes of memory.
// At 2x, every canvas is 4× larger than at 1x. With 10 cached pages this adds up
// to hundreds of MB on a 1x display. At 1.5x the canvases are 2.25× larger —
// still noticeably sharper than 1x, but using only 56% of the memory that 2x
// would require. On displays already at ≥1.5x (125%+ Windows scaling, retina)
// this script is a no-op.
//
// Old-Chromium blur fix:
// PDF.js snaps each page's CSS box down to a whole multiple of device pixels
// with the CSS round() function (setLayerDimensions in pdf.mjs), so the
// integer-pixel canvas maps 1:1 to the screen and stays crisp. CSS round() only
// landed in Chromium 111. On older builds PDF.js falls back to a plain calc(),
// leaving the page box at a fractional size — the canvas is then scaled by a
// non-integer factor and pages look slightly blurry. round() can't be polyfilled
// live (the box is recomputed by CSS on every zoom), so instead we raise the
// pixel-ratio floor to 2.0 on those builds: rendering at a higher resolution and
// letting the browser downscale (supersampling) hides the sub-pixel
// misalignment. We test round() support with the exact same expression PDF.js
// uses, so this kicks in precisely when PDF.js takes the blurry calc() path.
//
// Reference: https://blog.mozilla.org/nnethercote/2014/06/16/an-even-slimmer-pdf-js/
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
