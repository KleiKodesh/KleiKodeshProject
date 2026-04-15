# PDF.js Integration Instructions

## Opening PDFs

Always open PDFs through the router — navigate to `/pdf?file=<blobUrl>`. Never construct the viewer URL manually outside of `PdfViewPage.vue`.

## Required URL Parameters

Every PDF viewer URL must include these params (see `PdfViewPage.vue`):

- `locale=he` — forces Hebrew RTL UI (see Hebrew Localization below)
- `disableAutoFetch=false` — enables background page fetching
- `disableStream=false` — enables progressive streaming
- `disableRange=false` — enables range requests for large files
- `enableHWA=true` — enables GPU hardware acceleration
- `cMapPacked=true` — enables packed CMaps for faster Hebrew font loading

## Hebrew Localization (RTL)

`viewer.mjs` line ~658 is patched to default to Hebrew:

```js
lang: new URLSearchParams(window.location.search).get("locale") || "he"
```

This makes PDF.js load `locale/he/viewer.ftl`, set `dir="rtl"`, and spread pages right-to-left. Do not revert this line when upgrading PDF.js — reapply the patch.

## Theme Synchronization

After the iframe loads, call `syncPdfViewerTheme()` from `themes.ts` with a short delay:

```ts
const onIframeLoad = () => setTimeout(() => syncPdfViewerTheme(), 100)
```

`syncPdfViewerTheme()` does three things:
1. Sets `viewerCssTheme` (1=light, 2=dark) on `PDFViewerApplicationOptions`
2. Sets `color-scheme` and toggles the `dark` class on the iframe `<html>`
3. Injects all `--*-custom` CSS variables from the parent document into the iframe

`viewer.css` uses `var(--bg-primary-custom, fallback)` patterns so the injected variables override PDF.js defaults. `viewer-custom.css` adds scrollbar and sidebar overrides.

`initPdfThemeObserver()` (called in `main.ts`) watches for new PDF iframes via MutationObserver and auto-syncs them on load — so theme sync is automatic for dynamically added viewers.

## Sharpness

`pixel-ratio-override.js` forces `devicePixelRatio` ≥ 2 before PDF.js loads. It is referenced in `viewer.html` as the first script. Do not remove it.

## Upgrading PDF.js

When replacing `viewer.mjs` or `viewer.css` with a new PDF.js release, reapply these patches:

| File | What to patch |
|------|--------------|
| `viewer.mjs` ~line 658 | `localeProperties.lang` → URL param with `"he"` fallback |
| `viewer.mjs` ~line 16764 | `this.active = SidebarView.OUTLINE` (default sidebar to outline) |
| `viewer.mjs` ~line 17484 | Read `filename` URL param → `_contentDispositionFilename` |
| `viewer.mjs` ~line 5540 | `download()` → use `showSaveFilePicker()` with fallback |
| `viewer.css` ~line 7112 | `:root` color vars → `var(--*-custom, light-dark(...))` pattern |
| `viewer.html` | Add `<script src="pixel-ratio-override.js">` as first script |
| `viewer.html` | Add `<link rel="stylesheet" href="viewer-custom.css">` |

See `Pdf.js_Cotumizations.md` for full details on each patch.
