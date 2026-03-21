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
1. Find candidate books by matching the longest prefix of the query against book titles/paths.
2. For each candidate book, load its TOC entries and scan for a match against the remainder of the query.
3. Present results that include both the book and the matched TOC entry, so clicking one opens the book directly at that TOC section.

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

This is greedy from the right — Hebrew citations naturally read as `<book name> <chapter> <verse>`, so the TOC locator trails the book name. We trim from the right until books appear.

Edge cases:
- If even a single word matches multiple books, take all of them as candidates.
- If no prefix matches any book, the TOC fallback produces no results (graceful no-op).
- Minimum book fragment: 1 word. Minimum TOC fragment: 1 word (otherwise there's nothing to search in the TOC).

---

## Data Flow

### Phase 1 — Book candidates (in-memory, already loaded)

The `booksDataStore` already holds all books with normalized `searchPath`.
The existing word-filter logic in `useBooksFs` is reused here — no new DB query needed.

Output: a list of `BookRow` candidates (could be 1–N books).

### Phase 2 — TOC fetch (single DB query for all candidates)

Once we have the candidate book IDs, fire a single query for all of them at once:

```
SELECT te.id, te.parentId, te.bookId, tt.text
FROM tocEntry te
JOIN tocText tt ON tt.id = te.textId
WHERE te.bookId IN (?, ?, ...)
```

The `IN` clause is built dynamically from the candidate ID list. Results are a flat ordered list — no grouping step needed, all entries feed directly into a single `id → node` map for path building.

### Phase 2b — Build TOC full paths (in-memory)

TOC trees are arbitrarily deep. A leaf entry like `יד` is meaningless without its ancestors — we need the full intra-book TOC path `ד / יד` to match against the TOC fragment (the book part has already been resolved in Phase 1, so the book name is not included here).

After the query returns, build an `id → node` map. Then for each entry, walk up the `parentId` chain concatenating text segments to produce a normalized `tocSearchPath`. This mirrors exactly what `assignFullPaths` does for books.

The separator between TOC levels can be a space (since the user types the query as space-separated words), so the TOC fragment `ד יד` matches a path built as `ד / יד` after normalization strips the slashes.

### Phase 3 — TOC matching (in-memory)

For each TOC entry text, normalize it and check if it contains all words of the TOC fragment.
Same normalization as book search (`normalize()` from `src/utils/normalize.ts`).

All matching entries are kept — there can be multiple matches per book (e.g. the same chapter reference appears in different sections). Each match becomes its own result row: `{ book: BookRow, tocEntryId: number, tocText: string }`.

### Phase 4 — Result shape

A new result type `TocFsItem` is introduced alongside the existing `BookFsItem`:

```ts
type TocFsItem = {
  uid: string
  kind: 'toc'
  book: BookRow
  tocEntryId: number
  tocTitle: string   // the matched TOC entry's own text (e.g. "יד")
  tocPath: string    // full intra-book path (e.g. "ד / יד")
}
```

`tocTitle` and `tocPath` are kept separate so the result row can bind each to its own `<span>` independently.

---

## Navigation

When the user selects a `TocFsItem`, the tab navigates to the book view with the TOC entry pre-selected.

`tabStore.updateActiveTab` already accepts a `bookId`. We need to also pass a `tocEntryId` so `BookViewPage` can scroll to that entry on mount.

This requires checking whether `updateActiveTab` / the tab route data already supports a `tocEntryId` field, and adding it if not.

---

## UI

### Result list

TOC results are displayed in the same `BooksSearchResults` list. Each row has two separate spans:
- `<span class="item-title">` → `book.title`
- `<span class="item-path">` → `tocPath` (e.g. `ד / יד`)

```
[book icon]  בראשית          ← item-title (book.title)
             ד / יד          ← item-path (tocPath)
```

### Loading state

TOC queries are async (DB calls). While they run, a subtle loading indicator appears in the results area. The book-only results (empty) are replaced by a "מחפש..." state, then replaced by TOC results when ready.

### No results

If TOC fallback also finds nothing, show the normal "אין תוצאות" message.

---

## Where the Logic Lives

| Concern | Location |
|---|---|
| Query splitting heuristic | `src/utils/tocSearchSplit.ts` (pure util, no reactivity) |
| TOC path building | same util file |
| TOC matching logic | same util file |
| New SQL query | `src/db/queries.sql.ts` |
| All search orchestration + state | `src/components/books-fs/useBooksFsSearch.ts` — new composable, extracted from `useBooksFs.ts` |
| `useBooksFs.ts` | delegates search entirely to `useBooksFsSearch`, keeps only navigation/tree logic |
| New result types | `useBooksFsSearch.ts` |
| Result rendering | `BooksSearchResults.vue` — add a branch for `kind === 'toc'` |
| Navigation on select | `BooksFsPage.vue` — `onSelectBook` extended to handle `TocFsItem` |

---

## What Does NOT Change

- The normal book search path is untouched — TOC fallback only activates when book search returns zero results.
- `booksDataStore` is not modified.
- The debounce, normalization, and word-split logic are reused as-is.
- No new pages or routes are needed.

---

## Open Questions for You to Confirm

1. **`tocEntryId` in tab navigation** — does `updateActiveTab` already accept this, or does it need to be added to the tab data shape?
2. **Alt TOC structures** — should the fallback also search `alt_toc_entry`, or only the primary `tocEntry`?
3. **Result cap** — should TOC results be capped (e.g. top 20) to avoid flooding the list?
