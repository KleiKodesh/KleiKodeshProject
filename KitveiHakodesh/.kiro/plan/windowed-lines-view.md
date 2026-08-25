---
inclusion: fileMatch
fileMatchPattern: '**/book-view/lines/**|**/useBookViewSearch.ts|**/BookViewLinesContent.vue'
---

# Windowed Lines View

## Goal

Render the book-view lines pane from a DB window: fetch only the lines around the
reader's position, never the whole book, and never hold placeholders for the rest.

## Why not TanStack

`@tanstack/vue-virtual` is a *virtualizer*: it estimates every row's height, keeps a
`measurementsCache` entry per index, and builds prefix sums to answer "what is at
scrollTop". A windowed view needs none of that — the scroller gets a synthetic height
(`totalLines x nominal`), the scrollbar is an index map, and only the rendered slab is
laid out for real by the browser. Removing estimation removes TanStack's core, so
adapting its source is a rewrite, not an adjustment. Stock TanStack stays possible for
a data-only window; the position-mapped model is the better fit and is hand-rolled.

## The model

- Synthetic scroller height = `totalLines x nominal`. Thumb position = line fraction,
  not pixel truth (same contract as Word and Excel).
- One anchor: `(anchorLineIndex, offsetWithinAnchor)`. The slab renders from the anchor
  in normal flow; native scroll runs over it, so wheel/trackpad/momentum need no
  interception.
- Drift: real rows differ from nominal, so the thumb runs fast over a long scroll.
  Rebase on idle (scrollend/debounce) — recompute the slab's synthetic position from
  the current top line and write the compensating `scrollTop` in the same frame.
- Prepend: compensate in the same frame (`scrollTop += prependedHeight`) or rely on
  Chromium `overflow-anchor`. This is where every reference implementation's bugs live.

## Prior art to crib from

| Project | What to take | Where |
| --- | --- | --- |
| Element Web `ScrollPanel.tsx` | anchor = node + pixelOffset; relative `scrollBy` to avoid jumps; spacer quantized to 400px; no per-item measurement | element-hq/element-web |
| matrix-js-sdk `timeline-window.ts` | bounded window over a big sequence: `load(id)` / `paginate()` / `unpaginate()` at a `windowLimit` | matrix-org/matrix-js-sdk |
| SlickGrid `slick.grid.js` | index-mapped scrollbar; `th`/`h`/`ph`/`cj`; small-scroll vs page-jump discrimination; browser max element height | mleibman/SlickGrid (6pac fork maintained) |
| Zed `gpui/src/elements/list.rs` | `ListOffset { item_ix, offset_in_item }`; `with_uniform_item_height`; freeze height during scrollbar drag | zed-industries/zed |
| ag-Grid `infiniteRowModel/` (MIT) | block cache keyed by `floor(rowIndex / blockSize)`; LRU purge that never evicts displayed/focused blocks | ag-grid/ag-grid |

Native precedent for the index-mapped scrollbar: Word (background repagination),
Excel (thumb spans the used range in rows), RecyclerView, UITableView.

## Work

1. Rewrite `useBookViewLinesTable.ts`: drop `growTo` placeholders and the full-book
   backfill; keep the slot machinery and `getLinesPaged`; add visible-range-driven
   fetch, sparse storage (Map behind a shallow ref), and LRU eviction of far chunks.
2. Delete `useBookViewLinesBackfillGate.ts` — it exists only because the backfill
   competes with commentary queries. No backfill, no gate.
3. Replace the virtualizer in `BookViewLinesContent.vue` with spacer + slab + anchor.
4. Bounded-range fetches for consumers that assume any index is loaded: section
   selection, copy/export, scroll sync, commentary neighbor probe.
5. In-book search moves to the service (below).

Bug classes this deletes: estimate drift, `scrollToIndexWithRetry`, "measurementsCache
holds estimates but the row is not rendered", rAF-polling after scroll settles. Cost
accepted: a far jump or fast flick shows blank shells for one fetch.

## In-book search

No corpus in the frontend, so the JS scan in `useBookViewSearch.ts` has nothing to
scan. Decision: **scan in the service, do not build an index.**

- A trigram index answers substrings without reading text (Google Code Search / Zoekt;
  ~20% of corpus, positional ~1.2x) — **rejected on disk cost** against a 7.5 GB corpus.
- Scanning is fast enough. ripgrep's thesis: SIMD search runs at several GB/s. .NET 10
  AOT ordinal `IndexOf` is vectorized (AVX2/AVX-512). A 190k-line book is ~40 MB; a
  fused normalize-and-match pass lands in tens of ms, and the OS page cache makes
  repeat searches in the same book near-free — under the existing 150 ms debounce.
  It is also 10-50x faster than today's JS scan, which allocates a string per line.
- Reuse in-house parts: FtsLib `Tokenization/TokenStream.cs` (reused `_norm` buffer,
  nikud stripped) and `HtmlWordScanner.cs` for tag-aware normalization; the existing
  `ftsSearchStream` RPC for pushing frames; `getLinesWithContentPatternForBooks` as
  the SQL-layer precedent.
- Wire contract stays `{ lineIndex, occurrenceInLine }` — ordinals, not offsets, so the
  renderer keeps locating matches in the row it is already rendering. Only numbers
  cross the bridge. Stream frames with converging counts (PDF.js
  `updateMatchesCountOnProgress` behavior); never cap results.
- Semantics must match `stripHtmlForSearch` exactly: strip tags, collapse each entity
  to one sentinel char, strip U+0591-U+05C7, then substring match. Token/FTS matching
  is the wrong tool here — Telegram's word-based server search is the cautionary case
  for non-space-separated languages.
- Optional later: the corpus FTS index already on disk can pre-filter word/prefix
  queries to candidate lines (zero new disk); mid-word substrings fall back to the scan.

Tradeoff accepted: every search re-reads that book's text in the service (page-cached
after the first) instead of being in-memory after a long backfill. Flat memory, bounded
work per keystroke.
