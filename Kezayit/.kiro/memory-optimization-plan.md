# Memory & Reactivity Optimization Plan

## Status: Pending — needs testing before applying

These changes were identified, implemented, built successfully, then reverted pending proper testing.
Apply them one file at a time and verify the affected UI still works correctly after each change.

---

## What to do and why

Vue's `ref()` deep-proxies every nested object. For large collections (thousands of BookRow,
CategoryNode, LineItem, CommentaryGroup, BloomSearchResult objects) this creates thousands of
Proxy wrappers that consume memory and slow down property access. `shallowRef()` makes only the
top-level ref reactive — the array or map itself triggers updates when replaced, but individual
items are plain objects. This is correct for all these cases because items are never mutated
in-place after being set.

---

## Changes (apply and test one at a time)

### 1. `src/stores/booksDataStore.ts`

Change `allBooks`, `categoryMap`, `ROOT` from `ref` to `shallowRef`.
Change `allBooksMap` from `ref(new Map())` to a plain `let` module variable exposed via a getter.

Why `allBooksMap` doesn't need reactivity: it is only read after load, never mutated, and nothing
in the UI watches it directly — it's only passed as a parameter to `buildCommentaryGroups`.

Test: open the books browser (list, tiles, tree views all work), open a book from search results,
open a book from the full tree, verify commentary loads correctly.

### 2. `src/components/book-view/useLinesTable.ts`

Change `lines` and `hasCommentaries` from `ref` to `shallowRef`.

Because `lines` is mutated in-place per chunk (`lines.value[row.lineIndex] = ...`), shallowRef
won't auto-trigger after that mutation. Add `lines.value = lines.value` immediately after the
per-chunk mutation loop to force the trigger.

Test: open a large book, scroll through it — content must fill in as chunks load. Scroll to the
end, scroll back to the start. Prioritise (jump to a TOC entry mid-book) must still work.

### 3. `src/components/book-view/useToc.ts`

Change `tocEntries` from `ref` to `shallowRef` (altTocSections already uses shallowRef).

Test: TOC panel opens, entries render, clicking an entry scrolls to the correct line, TOC search
works, alt TOC structures (if present) work.

### 4. `src/components/book-view/useCommentary.ts`

Change `groups` from `ref` to `shallowRef`.

Test: select a line — commentary groups render. Navigate prev/next section. Commentary filter
(hide/show individual books) works. Commentary search works.

### 5. `src/components/search-db/useBloomSearch.ts`

Change `results` from `ref` to `shallowRef`.

Test: run a search — results render and are scrollable. Filter by book/category works. Run the
same search again — cache hit returns instantly. Streaming batches accumulate correctly
(each batch appends via `results.value = [...results.value, ...batch]` which is a full
reassignment, compatible with shallowRef).

---

## Risk notes

- `useLinesTable`: the `lines.value = lines.value` trigger is the only non-obvious part. If
  content stops filling in while scrolling, this is the first place to check.
- `booksDataStore` `allBooksMap` getter: any code that destructures `{ allBooksMap }` from the
  store will get the value at destructure time, not a reactive ref. This is fine since it's only
  read after `ensureLoaded()` resolves. Verify with `storeToRefs` — it should not be used on
  `allBooksMap` since it's not a ref.
- All other changes are straightforward replacements with no behavioral difference.
