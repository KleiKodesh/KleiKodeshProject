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

DONE (2026-10-28): one Core op, `linesSubstringSearch` (`SeforimDbLineSubstringSearch`):
the FTS index narrows (from two characters; merged text only) and the candidates are
confirmed natively (`SeforimDbLineTextScan`, one pass per line with the frontend's exact
normalization); when the index cannot answer (alternate version, book not indexed, a
too-common fragment) the same scanner reads the ranges' lines itself. Nothing is kept
anywhere. The frontend (`useBookViewSearch.ts`, `useCommentarySearch.ts`) sends ranges
and gets line ids and counts; it asks for the lines near the reader first, and an
extended query narrows from the previous or in-flight answer.
