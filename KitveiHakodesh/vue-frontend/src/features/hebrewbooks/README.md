# hebrew-books

HebrewBooks.org catalog browser. Search, download history, and PDF downloads via the C# WebView2 engine.

**HebrewBooksPage.vue** — main page with search bar, catalog list, and download history tab.

**HebrewBooksListItem.vue** — single catalog entry row with download and open actions.

**useHebrewBooks.ts** — search state, download triggering, and history management. All HebrewBooks interactions go through here.

**hebrewBooksCatalog.ts** — loads and parses `HebrewBooks.csv`. `searchHbCatalog` normalizes the query and filters by word-prefix matching across title, author, and tags. If the search behavior needs changing, start here.

**useHebrewBooks.ts** — search state, download triggering, and history management. History reads and writes go through `hebrewBooksHistoryStore`, not directly to IDB.

Downloads must go through the C# WebView2 engine — HebrewBooks blocks direct HTTP. Never use `fetch` or `HttpClient` to download PDFs from this source.
