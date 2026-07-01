# Dual Seforim Database (SQLite ATTACH)

Add support for a second parallel seforim SQLite database. The two databases are merged transparently using SQLite ATTACH so the frontend sees one unified dataset. Book IDs from each database are namespaced to avoid collisions. Full-text search runs two parallel FTS indexes and merges results. The user configures the second DB path in settings the same way as the primary.

## Branch

All implementation lives on branch **`feature/dual-seforim-db`**. The `master` branch does not contain any of the code changes — only this plan file.

**`master`** — clean, no dual-DB code. Only has this plan file (`dual-seforim-db.md`) as a reference document.

**`feature/dual-seforim-db`** — has all the implementation in a single commit on top of the same base as master.

To continue working on the remaining open tasks (smoke test, visual regression check on tree IDs > 10M):
```
git checkout feature/dual-seforim-db
```

To merge into master when ready to ship:
```
git checkout master
git merge feature/dual-seforim-db
```

To review all changes relative to master:
```
git diff master...feature/dual-seforim-db --stat
git diff master...feature/dual-seforim-db
```

This plan file lives on master and will always show you where things stand — the branch name, what's done, and what still needs testing.

## Tasks

### C# — DB layer

- [x] Add `AppSettings.LoadSecondDbPath` / `SaveSecondDbPath` persisted via registry under `Database\SecondPath`
- [x] Modify `DbAccess` to accept an optional second DB path; ATTACH it as `db2` on every pooled connection after opening
- [x] Update `DbHandler` to pass the second path through to `DbAccess`; handle `setSecondDbPath` and `pickSecondDbPath` bridge actions
- [x] Inject `window.__webviewSecondDbPath` and `window.__webviewSecondDbReady` into the WebView startup script in `AppViewer`
- [x] Handle `setSecondDbPath` and `pickSecondDbPath` in `AppViewer.OnMessageReceivedAsync`
- [x] Update `HandleReload` in `AppViewer` to re-inject both DB path variables and rebuild `DbAccess` with both paths

### C# — SQL queries (rewrite for ATTACH)

- [x] Rewrite `GET_ALL_CATEGORIES` to UNION `main.category` and `db2.category`, prefixing `db2` IDs with a namespace offset (+10,000,000) so they never clash with `main` IDs
- [x] Rewrite `GET_ALL_BOOKS` to UNION both schemas with the same ID offset applied to `bookId` and `categoryId`
- [x] Rewrite all remaining queries in `queries.sql.ts` to accept a `schema` parameter (`'main' | 'db2'`) so each query hits the correct database
- [x] Define the namespace offset constant in `src/webview-host/dualDatabaseIds.ts`; all SQL UNION queries inline the literal `10000000`

### C# — FTS dual index

- [x] Add a second `FtsIndexState` + `FtsIndexBuilder` instance in `SearchHandler` for the second DB path, using index directory `FtsIndex2`
- [x] `OnDbReady(primaryPath)` starts/resumes the primary index as before; if a second DB path is set, also calls `OnSecondDbReady(secondPath)` to start/resume the second index independently
- [x] `OnSecondDbReady(path)` mirrors `ExecuteOnDbReady` logic: version-stamp check, stale-segment detection, resume or fresh build — runs on the same actor thread
- [x] When the second DB path changes (user picks a new file or clears it via `setSecondDbPath`/`pickSecondDbPath`): stop and delete the second index, then call `OnSecondDbReady` with the new path (or do nothing if cleared) — wired via `DbHandler.OnSecondDbPathChanged` -> `SearchHandler.SetSecondDbPath`
- [x] When the second DB path is cleared: stop the second index build, delete `FtsIndex2`, push `ftsSecondIndexCleared` event so the frontend knows
- [x] Version-stamp check for the second index mirrors the primary: if the installed app version changed since the stamp was written, wipe and rebuild `FtsIndex2`
- [x] `HandleSearchStart` streams results from the primary index first; after the primary stream completes, streams results from the second index under the same `searchId` — sequential. Second index line ids are offset by 10,000,000 so the frontend routes them to `db2` schema. Skip second index if not ready.
- [x] `HandleGetProgress` reports primary index progress only (existing behavior unchanged); secondary builder pushes `ftsSecondIndexProgress` events independently
- [x] `HandleResetFtsIndex` resets both indexes
- [x] `DeleteAllCaches` deletes both index directories (`FtsIndex` and `FtsIndex2`)
- [x] `FtsIndexBuilder` uses instance `_state.ValidateIndex()` and `_state.WriteVersionStamp()` so primary and secondary each validate and stamp their own directory

### Vue — settings

- [x] Add `secondDbPath` to `settingsStore` (localStorage key `secondDbPath`) — empty string means no second DB
- [x] Add a second DB path picker row in `SettingsAdvancedPane.vue` below the primary DB picker, visible only when `isHosted`
- [x] Wire `setSecondDbPath` / `pickSecondDbPath` bridge actions through `bridge.ts`
- [x] Any change to the second DB path (add, replace, or remove) must be forwarded to C# via `setSecondDbPath` so `DbHandler` rebuilds `DbAccess` with the new path and `SearchHandler` starts, replaces, or deletes the secondary FTS index accordingly
- [ ] ~~Update the setup wizard `db` step to optionally allow picking a second DB~~ — second DB is an advanced option; settings page is the right place; skipping wizard change

### Vue — seforimDb.ts

- [x] Export `secondDbReady` ref, set from `window.__webviewSecondDbReady` on boot and updated when `secondDbPathPicked` push event arrives
- [x] Add `onSecondDbPathPicked` handler parallel to the existing `dbPathPicked` handler

### Vue — ID namespacing

- [x] Define `SECOND_DB_ID_OFFSET = 10_000_000` constant in a new `src/webview-host/dualDatabaseIds.ts` file
- [x] Export `isSecondDbId(id)`, `toSecondDbId(id)`, `fromSecondDbId(id)`, `schemaForId(id)` helpers
- [x] Audit every place in composables and stores that treats a `bookId`, `lineId`, `tocEntryId`, or `categoryId` as a raw DB integer and ensure offset stripping happens before any secondary query

### Vue — book catalog

- [x] `booksDataStore`: pass `hasSecondDb` flag to `GET_ALL_CATEGORIES` and `GET_ALL_BOOKS`; invalidate catalog when `secondDbReady` changes
- [ ] Verify category tree rendering handles IDs above 10,000,000 without visual regression

### Vue — book view

- [x] `useBookViewLinesTable`: strip offset from `bookId` and pass correct DB schema before querying `line` and `book` tables
- [x] `useBookViewToc`: strip offset before TOC and alt-TOC queries
- [x] `useCommentary` / `commentaryGroupBuilder`: strip offset from source `lineId`s before link queries
- [x] `commentaryNavigation`: strip offset from `mainBookId` and `commentaryBookId` before section-nav queries
- [x] `useCommentaryTocPaths`: derive schema from `bookId` on each group before TOC path queries
- [x] `useBookViewPinnedCommentary`: strip offset from `bookId` before default-commentator query
- [x] `dafYomiNavigation`: strip offset from `book.id` before TOC entry prefix query
- [x] `BookViewRelatedBooksDropdown`: strip offset from `bookId` and `targetBookId` before all five link queries

### Vue — full-text search

- [x] `useFullTextSearch`: split `enrichTocPaths` by schema — main and db2 line ids queried separately
- [x] `useFullTextSearchFilters`: strip offset from `result.lineId` before `GET_LINE_INDEX_FROM_LINE_ID`
- [x] `bookCatalogSearchTocHeuristics`: split `GET_TOC_TITLES_FOR_BOOKS` batch by schema

### Testing & cleanup

- [ ] Manual smoke test: load both DBs, verify book catalog shows books from both, open a book from each DB, verify commentary loads, verify FTS returns results from both
- [ ] Verify app reset wipes both FTS indexes and clears the second DB path from settings
- [x] Update `architecture.md` to document the dual-DB setup and the ID offset scheme
- [x] Update `database-schema.md` steering file to note the `db2` alias and offset convention
