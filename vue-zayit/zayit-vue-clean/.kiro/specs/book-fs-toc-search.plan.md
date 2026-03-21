# Plan: Book FS Search — TOC Fallback

## Problem

The current search in `BooksFsPage` filters books by their normalized `fullPath` (category path + title).
If the user types something like `בראשית ד יד`, the intent is:
- "בראשית" → the book name (or part of the path)
- "ד יד" → a TOC section within that book

The current search has no concept of this split — it treats the whole string as a book filter and returns nothing.

---

## Goal

When a book search yields no results, automatically attempt a **TOC-aware fallback**:
1. Find candidate books by trimming the query from the right until book matches appear.
2. Load TOC entries for all candidate books in batched DB queries.
3. Build full intra-book TOC paths in memory and match the TOC fragment against them.
4. Present all matching results (book + TOC entry), each navigating directly to that section.

---

## Heuristic: Query Splitting

The query is a single free-text string that implicitly encodes `<book fragment> <toc fragment>`.
There is no delimiter — we must infer the split point.

### Strategy: Right-trim to find book match

Given query words `[w1, w2, w3, w4]`:

1. Try the full query as a book search → if results exist, done (normal flow, no change).
2. If no results, trim one word from the **right** and retry:
   - `[w1, w2, w3]` → book search
   - `[w1, w2]` → book search
   - `[w1]` → book search
3. The **longest right-trimmed prefix** that returns at least one book match becomes the **book fragment**.
4. The trimmed words (the right-hand remainder) become the **TOC fragment**.

Edge cases:
- If even a single word matches multiple books, take all of them as candidates.
- If no prefix matches any book, the TOC fallback produces no results (graceful no-op).
- Minimum book fragment: 1 word. Minimum TOC fragment: 1 word.

---

## Data Flow

### Phase 1 — Book candidates (in-memory, already loaded)

`booksDataStore` holds all books with normalized `searchPath`. The word-filter runs in-memory — no DB query needed.

Output: a list of `BookRow` candidates (could be 1–N books).

### Phase 2 — TOC fetch (batched, streamed)

Candidate book IDs are sorted by `treeOrder` and chunked using `ceil(sqrt(n))` batch size. Each batch fires one DB query:

```sql
SELECT te.id, te.parentId, te.bookId, tt.text, l.lineIndex
FROM tocEntry te
JOIN tocText tt ON tt.id = te.textId
LEFT JOIN line l ON l.id = te.lineId
WHERE te.bookId IN (?, ?, ...)
ORDER BY te.id
```

`lineIndex` is fetched directly here — no secondary lookup needed at navigation time.
`ORDER BY te.id` ensures TOC entries within each book come back in insertion order (= tree order).
After each batch, nodes are sorted by `bookMap.treeOrder` to fix between-book ordering (SQLite's `IN` clause does not preserve parameter order).
Results stream per batch — the UI updates as each batch completes.

### Phase 2b — Strip book-title root nodes

Before building paths, root nodes whose `text` matches the book title are removed and their children re-parented to `null`. This prevents `tocDisplayPath` from starting with the book title.

### Phase 2c — Build TOC full paths (in-memory)

Two paths are built per node:
- `tocSearchPath` — normalized via `normalizeToc`: strips quotes, lowercases, inserts spaces around non-letter/digit chars so they become separate tokens. `יד:` → `יד :` — punctuation is preserved as its own token.
- `tocDisplayPath` — original text, ` / ` separated (for display)

TOC query words are normalized the same way via `normalizeTocWords` before matching.

### Phase 3 — TOC matching (in-memory)

TOC fragment words (normalized) are matched against `tocSearchPath` as an ordered subsequence:
- Each word must appear after the previous match (order enforced)
- No adjacency required — gaps are allowed
- Whole-word boundaries enforced — `ד` will not match inside `יד` or `כד`

The matching loop yields to the browser every 200 nodes via `await Promise.resolve()` to stay non-blocking.

### Phase 4 — Result shape

```ts
type TocFsItem = {
  uid: string
  kind: 'toc'
  book: BookRow
  tocEntryId: number
  tocLineIndex: number | null  // lineIndex fetched directly from DB — used for scroll on open
  tocTitle: string
  tocPath: string
}
```

---

## Debounce Strategy

Book search (Phase 1) is instant in-memory — runs on every keystroke, no debounce.
TOC fallback (Phase 2+) is debounced at 300ms — only fires when book search finds nothing.

---

## Caching

### LRU-1 cache (module-scope)

Single-entry cache keyed by `treeOrder`-sorted candidate book IDs. Avoids re-fetching when the user refines the TOC fragment without changing the book fragment.

```ts
let tocCache: { key: string; nodes: TocSearchNode[] } | null = null
```

Cache lives at module scope — persists across component remounts, resets on page reload.

### Stale result retention

On cache hit (same books, different TOC fragment), results are only replaced when the new filtered set is non-empty — backspacing keeps the last good match visible.

On cache miss (different books), results are cleared immediately before the new fetch starts.

> **TODO:** If the user types something with genuinely no TOC matches mid-query, current decision is to hold last results. Only show "אין תוצאות" when the query is too short or book split fails entirely.

### Performance note

The loading animation shows during TOC search (DB query latency — IPC round-trip to the C# host). The LRU-1 cache eliminates this on subsequent searches for the same book set. Results stream per batch.

Web Workers were investigated but are incompatible with `viteSingleFile` + `inlineDynamicImports: true`. See `.kiro/workers-research.md`.

---

## Result Ordering

Results match the tree's visual depth-first order:
- `BookRow.treeOrder` assigned during `assignFullPaths` — monotonically incrementing counter across depth-first walk (books before children at each node).
- `allBooks` in `booksDataStore` sorted by `treeOrder` after tree building.
- Candidate book IDs sorted by `treeOrder` before chunking.
- Within each batch, nodes sorted by `treeOrder` after `buildTocSearchPaths`.
- TOC entries within each book in `te.id` order via `ORDER BY te.id`.

---

## Navigation

`openTocEntryId` and `openTocLineIndex` added to `Tab` interface (both excluded from persistence).

When a `TocFsItem` is selected, `onSelectToc` calls `updateActiveTab` with:
- `bookId`, `openTocEntryId` (to highlight the correct TOC tree entry), `openTocLineIndex` (to scroll the content)

In `BookViewPage`:
- `openTocLineIndex` is read at mount into `initialLineIndex` ref — immediately available, no async wait.
- `openTocEntryId` is used to set `activeTocEntryId` once `tocEntries` load (for TOC tree highlight only).
- `initialLineIndex` is passed as a prop to `BookViewLinesContent`.

In `BookViewLinesContent`:
- `watch(loading, ..., { flush: 'post' })` fires after DOM update when lines finish loading.
- If `initialLineIndex` is set, calls `restoreScrollPos(initialLineIndex, 0)` — ignores saved state entirely.
- Otherwise falls back to saved scroll state from IDB.
- `openToc` conditional TOC visibility removed — TOC visibility always restored from saved state.

---

## UI

### Result list

TOC results display:
- title: `book.title / tocPath` (e.g. `ברכות / דף יד`) — slash separator matching TOC full path style
- path: category path same as regular book results (e.g. `סדר מועד`)

```
[book icon]  ברכות / דף יד         ← item-title
             סדר מועד              ← item-path (category path)
```

### Loading state

While TOC DB query is in flight, `tocSearching` is `true` and `LoadingAnimation` is shown.

### No results

"אין תוצאות" centered horizontally and vertically, shown only when query is too short or book split fails.

### Search bar placeholder

Rotating typewriter-style placeholder cycles through: `חיפוש ספר...` → `בראשית פרק ד` → `בבלי ברכות דף יד`. Pauses at each full string, then types the next. Animation pauses when the input has content.

---

## Where the Logic Lives

| Concern | Location |
|---|---|
| Query splitting heuristic | `src/utils/tocSearchSplit.ts` |
| TOC path building + normalization + matching | same |
| SQL query (with `lineIndex`) | `src/db/queries.sql.ts` (`GET_TOC_TITLES_FOR_BOOKS`) |
| `treeOrder` assignment | `src/components/books-fs/booksFsTree.ts` (`assignFullPaths`) |
| `allBooks` sorted by `treeOrder` | `src/stores/booksDataStore.ts` |
| LRU-1 cache + search orchestration | `src/components/books-fs/useBooksFsSearch.ts` |
| `useBooksFs.ts` | delegates search to `useBooksFsSearch`, keeps navigation/tree only |
| Result types (`TocFsItem`, `BookFsItem`) | `useBooksFsSearch.ts` |
| Result rendering | `BooksSearchResults.vue` |
| Navigation on select | `BooksFsPage.vue` (`onSelectToc`) |
| Scroll to line on open | `BookViewLinesContent.vue` (`initialLineIndex` prop, `watch(loading)`) |
| TOC tree highlight on open | `BookViewPage.vue` (watches `tocEntries` once for `activeTocEntryId`) |
| Tab shape | `tabStore.ts` — `openTocEntryId`, `openTocLineIndex`, excluded from persistence |

---

## What Does NOT Change

- Normal book search path is untouched — TOC fallback only activates when book search returns zero results.
- `booksDataStore` — `allBooks` now sorted by `treeOrder` (depth-first tree order) after loading.
- No new pages or routes needed.

---

## Open Questions

1. **Alt TOC structures** — should the fallback also search `alt_toc_entry`, or only the primary `tocEntry`?
2. **Result cap** — removed. All matching TOC entries are returned.
