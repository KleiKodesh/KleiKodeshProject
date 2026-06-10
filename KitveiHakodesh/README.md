# KitveiHakodesh

A Hebrew seforim reader. Mobile-first, strictly RTL, tabbed navigation. Features: book catalog browser, full-text search, book reader with linked commentary, PDF viewer, Hebrew/Aramaic dictionary, Hebrew calendar with zmanim, and a halachic unit converter.

## Two Parts

The project is split into a Vue 3 frontend and a C# backend.

**`vue-frontend/`** — all UI, state, and rendering. A standard Vite + Vue 3 + TypeScript project. This is where almost all feature work happens.

**`CSharpBackend/`** — hosts the frontend in a WebView2 control and provides native capabilities the web app cannot do on its own: opening SQLite databases from disk, native file pickers, PDF virtual hosts, and the Lucene-based full-text search index. It also owns the build pipeline — after every C# build it compiles the Vue app and copies the output into the deployment folder.

In production the two parts are inseparable. In development the Vue app runs standalone against a local dev server that mocks the database interface over HTTP, so you rarely need to touch the C# side for frontend work.

## Getting Started (Frontend)

```bash
cd vue-frontend
npm install
npm run dev
```

You need a `.db` file (the seforim SQLite database). Set the path in `vue-frontend/.env.development`:

```
DB_PATH=C:\path\to\your\seforim.db
```

The Vite dev server opens a SQLite connection to that file and serves queries at `/query`. The app auto-detects it's in dev mode and routes all database calls there instead of through the C# host.

## Project Structure

```
KitveiHakodesh/
├── vue-frontend/          — Vue 3 app (all UI work happens here)
│   ├── src/
│   │   ├── components/    — feature folders (one folder per page/feature)
│   │   ├── composables/   — shared composables used across features
│   │   ├── stores/        — Pinia stores (state + persistence)
│   │   ├── host/          — database access and C# bridge
│   │   ├── utils/         — pure utility functions
│   │   ├── theme/         — theme system (CSS vars, store, presets)
│   │   ├── App.vue        — root component
│   │   └── main.ts        — app entry point
│   ├── public/            — static assets (PDF.js, dictionary DBs, fonts)
│   ├── server/            — standalone dev SQLite server (optional)
│   └── scripts/           — database maintenance scripts
└── CSharpBackend/         — .NET 4.8 host (WebView2, SQLite, search, PDF)
```

Every folder under `src/` has its own `README.md` — read it before editing files in that folder. The READMEs answer "which file do I edit?", "what do I import from where?", and "what rules apply here?".

## How to Make Common Changes

### Add a new page

1. Create a feature folder under `vue-frontend/src/components/` named after the feature (e.g. `my-feature/`).
2. Create `MyFeaturePage.vue` inside it. Pages are always named `*Page.vue`.
3. Register the route in `AppPageView.vue` (maps route strings to page components).
4. Add a navigation entry to both `HomePage.vue` and `AppTitleBarNavDropdown.vue` — these two lists must always be kept in sync.
5. If it's a singleton route (only one tab allowed), use `tabStore.navigateToSingleton('/my-feature')` to navigate to it. Add it to the singleton list in `tabStore`.
6. Add a `README.md` to the new folder.
7. Update `architecture.md` in `.kiro/steering/` to include the new route and feature folder.

### Add a new setting

1. Add a key constant to `KEYS` in `vue-frontend/src/utils/persistence.ts`.
2. Add the reactive ref and watcher to `settingsStore.ts` — follow the existing pattern.
3. Expose it from the store. Components read settings from the store, never from `persistence.ts` directly.

### Add a new SQL query

All SQL lives in `vue-frontend/src/webview-host/queries.sql.ts` as named string constants. Add your query there and import it in the composable that needs it. Never write inline SQL anywhere else.

### Add a new icon

Use Iconify via `@iconify-prerendered/vue-fluent`. Import the icon component by name — e.g. `import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'`. Never inline SVGs. Use size `20` for UI controls, `24` for larger touch targets. Use `Regular` weight by default, `Filled` for active/selected states.

### Change how data is fetched

All database access goes through `vue-frontend/src/webview-host/db.ts`. Call `query<T>(sql, params)` — it routes to the C# host in production and the Vite dev middleware in development. Never call `fetch` against the database directly from a component or composable.

## Key Rules

These are the constraints most likely to cause bugs if ignored.

**RTL layout** — the app is strictly right-to-left. Physical right = reading start. `inline-start` = physical right. `inline-end` = physical left. When positioning elements, think in physical screen coordinates, not logical ones. Read `app.md` in `.kiro/steering/` for the full cheatsheet.

**All UI text must be Hebrew** — no English strings in templates, placeholders, tooltips, error messages, or any visible UI. This is a hard rule.

**No inline SQL** — all SQL goes in `queries.sql.ts`. No exceptions.

**No direct persistence access** — components and composables never import from `persistence.ts` or call `localStorage`/IndexedDB directly. Everything goes through a store.

**No business logic in components** — components receive props and emit events. Data fetching and state logic belong in composables and stores.

**Prettier compatibility** — the project uses Prettier with `printWidth: 100`. Never put multi-statement logic or long ternaries inline in Vue template attribute bindings — Prettier will reformat them into multiline strings that the Vue compiler rejects. Extract to a named function or computed instead.

**File size limits** — Vue components: 350 lines max. Composables: 300 lines max. Utils: 250 lines max. Stores: 300 lines max. If an edit pushes a file over its limit, split it before finishing.

## How the Frontend and Backend Communicate

Before the page loads, the C# host injects a JavaScript bridge that exposes async functions on `window`. The Vue app calls these through `src/webview-host/db.ts` and `src/webview-host/bridge.ts` — the transport detail is hidden from the rest of the app.

- SQL queries → `window.__webviewQuery` → C# `DbHandler` → SQLite → rows returned as JSON
- File operations, PDF management, search → `window.__webviewAction` → appropriate C# handler
- C# pushes events back (indexing progress, download complete, DB ready) → `window.__onWebviewEvent` → `onWebviewEvent()` in `db.ts`

In dev mode, SQL calls go to the Vite middleware instead. Everything else (file pickers, PDF hosts) has dev-mode fallbacks in `bridge.ts`.

## C# Projects

| Project                | Role                                                                      |
| ---------------------- | ------------------------------------------------------------------------- |
| `KitveiHakodeshLib`           | WebView2 host, message bridge, SQLite access, PDF handling, file I/O      |
| `SearchEngine`         | Lucene-based full-text search index builder and searcher                  |
| `KitveiHakodeshDemoApp`       | Standalone WinForms harness for running the Vue app outside of production |
| `DocumentLocator`      | NTFS MFT file index Windows service + named-pipe client for local file search |

See `CSharpBackend/README.md` for C# setup and build instructions.
