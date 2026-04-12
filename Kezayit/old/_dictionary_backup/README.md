# dictionary/

Dictionary and reference books browser. Singleton route `/dictionary`, navigated via `tabStore.navigateToSingleton('/dictionary')`.

## Files

`DictionaryPage.vue` — the page. Split layout: 220px left panel (search results or book shelf) + right panel (entry content). Header has a search input. Filter tabs appear above the split when results exist.

`useDictionarySearch.ts` — all search logic. Debounces the query (300ms), runs `SQL.SEARCH_DICTIONARY_ENTRIES` with a LIKE param, extracts headwords from raw HTML content per book format, manages `activeBookId` filter, fetches full entry lines on selection. Returns reactive state directly (no `.value` needed in templates).

`useDictionary.ts` — loads all reference books from categories 75 and 1220 (and their sub-categories) for the book shelf shown when no search is active. Groups into `DictionarySection[]` by sub-category title.

`DictionarySearchResults.vue` — the search results panel. Owns the filter tabs and the scrollable result list. Each row expands inline on click to show the full entry via `DictionaryEntryView`. Emits `toggle`, `update:activeSource`, and `openInViewer` — no navigation logic inside.

`DictionaryEntryView.vue` — renders a single entry's HTML content with styled `<b>`, `<big>`, `<h3>`, `<small>`, and `<span dir="ltr">` tags. Has a toolbar showing the source book name and an "open in viewer" button.

`DictionaryBookShelf.vue` — the empty-state book list. Shows all reference books grouped by category. Clicking a book emits `open(bookId, title)` which the page handles by navigating to `/book-view`.

## Searchable books and entry formats

| Book             | id   | Entry format                                  | Headword extraction |
| ---------------- | ---- | --------------------------------------------- | ------------------- |
| ספר הערוך        | 473  | One line, `<b><big>WORD</big></b> ...`        | `<big>` content     |
| הפלאה שבערכין    | 471  | One line, `<b>WORD</b> ...`                   | first `<b>` content |
| ספר השרשים לרד"ק | 6105 | Two lines: `<h3>ROOT</h3>` + content          | `<h3>` content      |
| אוצר לעזי רש"י   | 472  | One line, `N / (source) / <b>WORD</b><br>...` | first `<b>` content |

## SQL queries

`SQL.SEARCH_DICTIONARY_ENTRIES` — parameterized LIKE search across all four books, returns up to 200 results ordered by book then lineIndex.

`SQL.GET_DICTIONARY_ENTRY_LINES` — fetches 1 or 2 lines by bookId + lineIndex range (2 lines for ספר השרשים, 1 for all others).

`SQL.GET_DICTIONARY_BOOKS` — all books under cats 75 and 1220 with authors, for the shelf.

## Navigation

Clicking "open in viewer" calls `tabStore.updateActiveTab({ route: '/book-view', bookId, openTocLineIndex })` — navigates in-place to the book at the entry's line position.
