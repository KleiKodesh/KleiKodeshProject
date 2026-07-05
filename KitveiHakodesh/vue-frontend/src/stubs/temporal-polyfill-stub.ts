// @hebcal/core imports temporal-polyfill/global for its side effect (installing
// Temporal on globalThis). We only want the polyfill on WebView2 builds older
// than 1.0.3912 (Chromium 137 and below) that don't ship native Temporal.
//
// On Chromium 138+ the native Temporal already exists; running the polyfill
// would overwrite it with the polyfill implementation. The original stub was
// an empty export that unconditionally skipped the polyfill, which caused
// "ReferenceError: Temporal is not defined" for users on older WebView2 versions.
//
// This replacement imports the polyfill's individual named exports and installs
// them only if native Temporal is absent — safe on both old and new WebView2 builds.
import { Temporal as _TemporalPolyfill, toTemporalInstant as _toTemporalInstantPolyfill } from 'temporal-polyfill'

if (typeof globalThis.Temporal === 'undefined') {
  Object.defineProperty(globalThis, 'Temporal', {
    value: _TemporalPolyfill,
    configurable: true,
    writable: true,
    enumerable: false,
  })
  Object.defineProperty(Date.prototype, 'toTemporalInstant', {
    value: _toTemporalInstantPolyfill,
    configurable: true,
    writable: true,
    enumerable: false,
  })
}
export {}
