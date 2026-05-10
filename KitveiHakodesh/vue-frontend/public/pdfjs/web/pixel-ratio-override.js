// Force minimum 1.5x pixel ratio for sharp PDF rendering on low-DPI displays.
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
// Reference: https://blog.mozilla.org/nnethercote/2014/06/16/an-even-slimmer-pdf-js/
// "on Mac the canvas data is stored within the process... my MacBook has a retina
// screen, which means the canvases used have approximately four times as many pixels"
(function () {
  const original = window.devicePixelRatio || 1;
  const enhanced = Math.max(original, 1.5);
  if (enhanced !== original) {
    Object.defineProperty(window, 'devicePixelRatio', {
      get: function () { return enhanced; },
      configurable: true,
    });
  }
})();
