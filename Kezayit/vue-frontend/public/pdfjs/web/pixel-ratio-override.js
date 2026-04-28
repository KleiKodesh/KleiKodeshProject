// Force minimum 2x pixel ratio for sharp PDF rendering on low-DPI displays.
// Must be loaded before PDF.js initializes.
(function () {
  const original = window.devicePixelRatio || 1;
  const enhanced = Math.max(original, 2);
  if (enhanced !== original) {
    Object.defineProperty(window, 'devicePixelRatio', {
      get: function () { return enhanced; },
      configurable: true,
    });
  }
})();
