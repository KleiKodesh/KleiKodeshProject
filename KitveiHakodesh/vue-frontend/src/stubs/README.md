# stubs

Module stubs used by the Vite alias map in `vite.config.ts` to replace third-party package entry points at build time.

**temporal-polyfill-stub.ts** — replaces `temporal-polyfill/global` (imported as a side effect by `@hebcal/core/molad.js` to install `Temporal` onto `globalThis`). The stub installs the polyfill only when native `Temporal` is absent — i.e. on WebView2 builds older than 1.0.3912 (Chromium 137 and below). On Chromium 138+ the native `Temporal` is used as-is, keeping the 118KB polyfill out of the bundle. Without this guard, users on older WebView2 versions see `ReferenceError: Temporal is not defined` whenever the calendar page loads a week that includes a molad entry.

When upgrading `@hebcal/core`, verify that `molad.js` in the new version still imports `temporal-polyfill/global` — if it stops doing so, or switches to a different polyfill entry point, update the alias key in `vite.config.ts` accordingly.
