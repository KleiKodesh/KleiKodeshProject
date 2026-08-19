# html-view

HTML file viewer for displaying local HTML and HTM files.

**HtmlViewPage.vue** — renders HTML files in an iframe served via a C# virtual host. Handles local HTML/HTM files opened via the file picker. Session restore is handled by `localFileStore` at app boot — do not add restore logic here. Displays a loading state with a 6-second timeout; if the iframe fails to load within that time, shows an error message with a retry button. Supports the PDF page filters setting for visual adjustments. When the tab has `isOtzariaAddin` set, it activates the addin bridge after iframe load. Propagates the app-wide scrollbars mode (static / auto-hide — Ctrl+Shift+H, reading mode, or settings) into the iframe via `useIframeScrollbarsAutoHide` — cross-origin frames receive it as an `htmlViewScrollbars` postMessage handled by the C#-injected IframeScrollScript.

**useOtzariaAddinBridge.ts** — composable owning the addin bridge lifecycle: injects the SDK stub into the iframe after load, listens for `otzaria-call` postMessages, replies with the official `{ success, data, error }` envelope, fires `plugin.boot` and pushes `theme.changed` events.

**otzariaAddinBridgeStub.ts** — the ES5 script injected into the addin iframe. Recreates the official Otzaria plugin SDK: `window.Otzaria` (`call` resolves with the envelope, never rejects) plus the legacy `window.OtzariaAddin` alias (`call` resolves data / rejects on error).

**otzariaAddinDataQueryApi.ts** — the API surface served to addins, deliberately restricted to data-query methods (app info/theme/locale, library.findBooks/getBookMetadata/getBookToc/getBookContent/getTree, settings read allowlist, per-addin storage). Every other official namespace (reader, navigation, ui, network, database, notes, history, fs, ...) is rejected with `PERMISSION_DENIED`. Response shapes follow the official plugin SDK reference (`docs/plugin-sdk/API_REFERENCE.md` in the otzaria/otzaria repository): `bookId` is the human-readable title, `id` the numeric database id. When enabling a new method, add it here and report its permission in `GRANTED_PERMISSIONS`.

**otzariaAddinStorage.ts** — per-addin sandboxed IndexedDB (`app-addin-storage-<addinId>`) backing the `storage.*` API; grants no access to app data.

Text files (`.txt`) are handled by the separate `txt-view` feature, not here.
