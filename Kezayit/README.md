# Kezayit — כזית Seforim Viewer (Vue 3 Frontend)

The Vue 3 + TypeScript frontend for the Kezayit seforim viewer. It runs inside a WebView2 control hosted by `KezayitLib` and is displayed as a task pane in Word when the user clicks the **כזית** ribbon button.

## How It Integrates with the VSTO Add-in

`KleiKodeshVsto` creates a `KezayitLib.AppViewer` (a WinForms `UserControl`) and docks it as a task pane. `AppViewer` initializes WebView2, maps the virtual host `kezayit-vue-app` to the `kezayit/` folder in the install directory, and loads `index.html`. The Vue app then communicates with the C# backend via a `window.__webviewAction` / `window.__onWebviewEvent` message bridge.

In development the app runs against a Vite dev server (`npm run dev`).

## Folder Structure

```
Kezayit/
├── src/
│   ├── App.vue                  — Root component; router outlet + global layout
│   ├── main.ts                  — App entry; initialises stores, mounts app
│   ├── components/
│   │   ├── book-view/           — Book reader (TOC, line list, commentary sync)
│   │   ├── pdf/                 — PDF viewer (PDF.js integration)
│   │   ├── search-db/           — Bloom filter search UI
│   │   ├── hebrew-books/        — HebrewBooks.com browser & downloader
│   │   ├── hebrew-calendar/     — Hebrew calendar widget
│   │   ├── dictionary/          — Hebrew dictionary lookup
│   │   ├── settings/            — User preferences UI
│   │   ├── workspace/           — Workspace switcher
│   │   └── layout/              — App shell (title bar, tab bar, page container)
│   ├── stores/                  — Pinia state (persisted to IndexedDB)
│   │   ├── tabStore.ts          — Open tabs per workspace
│   │   ├── workspaceStore.ts    — Named workspaces
│   │   ├── settingsStore.ts     — Font, zoom, diacritics, theme preferences
│   │   ├── bookViewStore.ts     — Scroll position, TOC state
│   │   ├── booksDataStore.ts    — Books catalogue from SQLite
│   │   └── searchCacheStore.ts  — Search result cache
│   ├── composables/
│   │   ├── useAppNavigation.ts  — Tab/route navigation helpers
│   │   ├── useVirtualScrollerKeys.ts — Keyboard nav for virtual lists
│   │   ├── useZoom.ts           — App-wide zoom control
│   │   └── useTextSelectionKeys.ts  — Text selection keyboard shortcuts
│   ├── host/
│   │   ├── bridge.ts            — File operations (pickFile, restorePdf, disposePdfHost)
│   │   ├── db.ts                — SQL query execution; dev-mode mock
│   │   └── queries.sql.ts       — All SQL strings (single source of truth)
│   └── utils/
│       ├── idbPersistence.ts    — IndexedDB helpers (get/set/delete per workspace)
│       └── ...
├── public/
│   ├── dictionary.db            — SQLite dictionary database
│   ├── themes.json              — Theme definitions
│   ├── HebrewBooks.csv          — HebrewBooks catalogue seed data
│   └── pdfjs/                   — PDF.js worker & viewer assets
├── server/
│   └── index.js                 — Express dev server (proxies API calls in dev mode)
├── scripts/                     — Node.js scripts for database maintenance
│   ├── create-dictionary-db.cjs — Builds the dictionary SQLite DB
│   ├── import-aramaic.cjs       — Imports Aramaic entries
│   ├── check-hebrewbooks.cjs    — Validates HebrewBooks catalogue
│   └── ...
├── CSharpBackend/               — C# backend (see sub-READMEs)
│   ├── KezayitLib/              — WebView2 host + message bridge
│   └── BloomSearchEngineLib/    — Bloom filter search engine
├── vite.config.ts               — Vite build config
└── package.json
```

## State Persistence

All user state is stored in **IndexedDB** (via `idbPersistence.ts`), keyed by workspace ID. This means each named workspace has its own independent set of open tabs, last-read positions, and settings.

## Host Bridge

When running inside WebView2, `host/db.ts` and `host/bridge.ts` call `window.__webviewAction(name, args)` which is injected by `KezayitLib/Bridge/JsBridge.cs`. Responses arrive via `window.__onWebviewEvent`. In dev mode (no WebView2 host) these calls fall back to mock data or the Express dev server.

## Build

```bash
npm install
npm run dev      # development server
npm run build    # production build → dist/
```

The production build is copied into the VSTO package by `KleiKodeshVsto/build-vue-app.ps1`.
