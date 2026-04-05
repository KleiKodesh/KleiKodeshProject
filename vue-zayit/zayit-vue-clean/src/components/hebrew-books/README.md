# hebrew-books

HebrewBooks.org catalog browser. Search, download history, and PDF downloads via the C# WebView2 engine.

**HebrewBooksPage.vue** — main page with search bar, catalog list, and download history tab.

**HebrewBooksListItem.vue** — single catalog entry row with download and open actions.

**useHebrewBooks.ts** — search state, download triggering, and history management. All HebrewBooks interactions go through here.

**hebrewBooksCatalog.ts** — loads and parses `HebrewBooks.csv`. `searchHbCatalog` uses `scoreMatch` from `fuzzyMatch.ts` across title, author, and tags, sorted by score then alphabetically. If the search behavior needs changing, start here.

**hebrewBooksHistory.ts** — persists and retrieves download history via `tabStore`. Do not write history directly to IDB.

Downloads must go through the C# WebView2 engine — HebrewBooks blocks direct HTTP. Never use `fetch` or `HttpClient` to download PDFs from this source.
