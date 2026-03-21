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
2. Load TOC entries for all candidate books in a single DB query.
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

### Phase 2 — TOC fetch (batched, streamed)

Once we have the candidate book IDs, they are sorted by `treeOrder` and chunked using `ceil(sqrt(n))` batch size. Each batch fires one DB query:

```sql
SELECT te.id, te.parentId, te.bookId, tt.text
FROM tocEntry te
JOIN tocText tt ON tt.id = te.textId
WHERE te.bookId IN (?, ?, ...)
ORDER BY te.id
```

`ORDER BY te.id` ensures TOC entries within each book come back in insertion order (= tree order). After each batch, nodes are sorted by `bookMap.treeOrder` to fix between-book ordering within the batch (SQLite's `IN` clause does not preserve parameter order). Results stream per batch — the UI updates as each batch completes rather than waiting for all books to load.

### Phase 2b — Strip book-title root nodes

Before building paths, root nodes whose `text` matches the book title are removed and their children are re-parented to `null`. This prevents `tocDisplayPath` from starting with the book title (which would produce `ברכות — ברכות / דף יד`).

### Phase 2c — Build TOC full paths (in-memory)

TOC trees are arbitrarily deep. Each entry needs its full intra-book path (e.g. `דף יד`) to match against the TOC fragment. The book name is excluded — it was already resolved in Phase 1.

Two paths are built per node:
- `tocSearchPath` — normalized via `normalizeToc`: strips quotes, lowercases, and inserts spaces around non-letter/digit characters (`:`, `.`, `-`, etc.) so they become separate tokens. This means `יד:` normalizes to `יד :` — the word `יד` is findable without punctuation, but the `:` is preserved as its own token so `יד:` in the query only matches entries with a colon, not a dot.
- `tocDisplayPath` — original text, ` / ` separated (for display)

TOC query words are normalized the same way via `normalizeTocWords` before matching.

### Phase 3 — TOC matching (in-memory)

TOC fragment words (normalized) are matched against `tocSearchPath` as an ordered subsequence:
- Each word must appear after the previous match (order enforced)
- No adjacency required — gaps are allowed
- Whole-word boundaries enforced — `ד` will not match inside `יד` or `כד`

So `פרק א פסוק ג` will not match a path `פרק ג פסוק א`.
And `יד.` will match `יד.` but not `יד:` — punctuation type is preserved and discriminating.

All matching entries are kept — multiple matches per book are all included as separate result rows.

### Phase 4 — Result shape

```ts
type TocFsItem = {
  uid: string
  kind: 'toc'
  book: BookRow
  tocEntryId: number
  tocTitle: string   // the matched TOC entry's own text
  tocPath: string    // full intra-book display path (e.g. "דף יד")
}
```

---

## Debounce Strategy

Book search (Phase 1) is instant in-memory — it runs on every keystroke with no debounce.
TOC fallback (Phase 2+) is debounced at 300ms — it only fires when the book search finds nothing, and involves an async DB query.

This means book results appear immediately as the user types, and the TOC fallback kicks in after a short pause only when needed.

---

## Caching

### LRU-1 cache (module-scope)

The DB query + path building is the expensive step. A single-entry cache keyed by sorted candidate book IDs avoids re-fetching when the user refines the TOC fragment without changing the book fragment (e.g. typing `בראשית פרק` then `בראשית פרק א` — same candidate books, cache hit).

```ts
let tocCache: { key: string; nodes: TocSearchNode[] } | null = null
```

Cache lives at module scope — persists across component remounts, resets on page reload. Zero memory bloat: only one entry ever stored.

### Stale result retention

Results are only replaced when the new filtered set is non-empty. This means backspacing through the TOC fragment keeps the last good match visible rather than flashing empty.

> **TODO:** Edge case — if the user types something that genuinely has no TOC matches mid-query, should it show empty or hold the last results? Current decision: hold last results. Only show "אין תוצאות" when the query is too short or the book split fails entirely. Revisit if this feels wrong in practice.

### Performance note

The loading animation shows during TOC search (DB query latency — IPC round-trip to the C# host). The LRU-1 cache eliminates this on subsequent searches for the same book set. Results stream per batch so the user sees partial results as they arrive. The matching loop yields to the browser every 200 nodes via `await Promise.resolve()` to keep the UI responsive with large result sets.

Web Workers were investigated as a solution but are incompatible with `viteSingleFile` + `inlineDynamicImports: true`. See `.kiro/workers-research.md` for full details.

## Result Ordering

Results match the tree's visual depth-first order:
- `BookRow.treeOrder` is assigned during `assignFullPaths` in `booksFsTree.ts` — a monotonically incrementing counter across the depth-first walk (books before children at each node).
- `allBooks` in `booksDataStore` is sorted by `treeOrder` after tree building.
- Candidate book IDs are sorted by `treeOrder` before chunking, so batches arrive in tree sequence.
- Within each batch, nodes are sorted by `treeOrder` after `buildTocSearchPaths` (SQLite's `IN` clause does not preserve parameter order).
- TOC entries within each book are in `te.id` order (insertion order = tree order) via `ORDER BY te.id` in the query.

---



`openTocEntryId` added to the `Tab` interface (excluded from persistence). When a `TocFsItem` is selected, `updateActiveTab` is called with `bookId`, `openToc: true`, and `openTocEntryId`. `BookViewPage` watches `tocEntries` once on mount — when entries load, it finds the entry by ID, scrolls to its `lineId`, and stops watching.

---

## UI

### Result list

TOC results display:
- title: `book.title / tocPath` (e.g. `ברכות / דף יד`) — slash separator matching the TOC full path style
- path: category path same as regular book results (e.g. `סדר מועד`)

```
[book icon]  ברכות / דף יד         ← item-title
             סדר מועד             ← item-path (category path)
```

### Loading state

While the TOC DB query is in flight, `tocSearching` is `true` and `LoadingAnimation` is shown in place of results.

### No results

"אין תוצאות" is shown only when the query is too short or no book split was possible.

---

## Where the Logic Lives

| Concern | Location |
|---|---|
| Query splitting heuristic | `src/utils/tocSearchSplit.ts` |
| TOC path building | same |
| TOC ordered subsequence matching | same (`matchWords`) |
| Punctuation-aware normalization | same (`normalizeToc`, `normalizeTocWords`) |
| New SQL query | `src/db/queries.sql.ts` (`GET_TOC_TITLES_FOR_BOOKS`) |
| LRU-1 cache + search orchestration | `src/components/books-fs/useBooksFsSearch.ts` |
| `useBooksFs.ts` | delegates search to `useBooksFsSearch`, keeps navigation/tree only |
| New result types | `useBooksFsSearch.ts` |
| Result rendering | `BooksSearchResults.vue` |
| Navigation on select | `BooksFsPage.vue` (`onSelectToc`) |
| Scroll to TOC entry on open | `BookViewPage.vue` (watches `tocEntries` once on mount) |
| Tab shape | `tabStore.ts` — `openTocEntryId?: number`, excluded from persistence |

---

## What Does NOT Change

- The normal book search path is untouched — TOC fallback only activates when book search returns zero results.
- `booksDataStore` — `allBooks` is now sorted by `treeOrder` (depth-first tree order) after loading.
- No new pages or routes are needed.

---

## Open Questions

1. **Alt TOC structures** — should the fallback also search `alt_toc_entry`, or only the primary `tocEntry`?
2. **Result cap** — removed. All matching TOC entries are returned.
