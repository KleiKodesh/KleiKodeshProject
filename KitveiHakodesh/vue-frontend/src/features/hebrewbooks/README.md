# hebrew-books

HebrewBooks.org catalog browser. Search is backed by a SQLite database on the C# side. Downloads go through the C# WebView2 engine.

**HebrewBooksPage.vue** — main page with search bar, virtual list, and download history.

**HebrewBooksListItem.vue** — single catalog entry row with open and download actions.

**useHebrewBooks.ts** — all state and actions for this feature. Shows history on load (from `hebrewBooksHistoryStore`), runs search via `searchHbCatalog` on every keystroke (debounced 200ms).

**hebrewBooksCatalog.ts** — thin bridge wrapper. `searchHbCatalog(term)` calls `hbSearch` in `bridge.ts` which sends the query to C# (`HebrewBooksDb.Search`). Returns up to 200 results sorted by title. The database is `Resources/HebrewBooks.db` deployed with the C# lib.

Downloads must go through the C# WebView2 engine — HebrewBooks blocks direct HTTP. Never use `fetch` or `HttpClient` to download PDFs from this source.

History is stored in `app-hb-history` IDB, capped at 25 entries, and kept in memory after the first read so all subsequent accesses are synchronous.
