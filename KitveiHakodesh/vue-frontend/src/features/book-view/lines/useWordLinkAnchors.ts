/**
 * Lazy, viewport-driven loader for word-level link anchors (link_anchor, schema v2+).
 *
 * Same shape as useBookViewNotes: the caller provides a `getVisibleLineIds` getter;
 * whenever the visible set changes, a 100ms debounce fires ONE batched query for the
 * lineIds not yet requested. Initial render is never blocked and lines the user never
 * scrolls to are never queried.
 *
 * On schema-v1 DBs (no link_anchor table) the seforimApi layer short-circuits to []
 * after the first probe, so this composable stays a per-scroll no-op there — zero
 * queries, zero render-path cost (getWordLinkAnchorsForLine returns a shared empty
 * array and the renderers skip the splice entirely).
 */
import { ref, watch } from 'vue'
import { getWordLinkAnchorsForLines, getWordLinkTargetsForBook } from '@/webview-host/seforimApi'
import { buildWordLinkTreatments, type WordLinkTreatment } from './wordLinkAnchors'
import type { WordLinkAnchor } from '@/webview-host/queries.types'

const EMPTY: WordLinkAnchor[] = []

/**
 * Two wiring modes, matching the two existing lazy-annotation patterns:
 *   - pass `getVisibleLineIds` and loading is watch-driven (lines view, like useBookViewNotes)
 *   - omit it and call the returned `scheduleWordLinkAnchorsLoad` from the component's
 *     virtualizer watcher (commentary view, like useCommentaryNotes.scheduleNotesLoad)
 */
export function useWordLinkAnchors(getVisibleLineIds?: () => number[]) {
  // Retains anchors for every line scrolled past, deliberately un-evicted: the rows
  // are a handful of small ints per anchor, so the footprint is negligible next to the
  // line content itself. If the lines are ever evicted from their own store, evict
  // these (and `requested`) alongside them — not before.
  const anchorsByLine = ref<Map<number, WordLinkAnchor[]>>(new Map())
  // lineIds already sent to (or in flight toward) the DB — each is queried once.
  const requested = new Set<number>()
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  // Per-source-book fallback treatments (rank by ascending target book id, plus the
  // sign-vocabulary guard — see buildWordLinkTreatments). Promise-cached so each book's
  // targets are queried once and every batch shares the same, stable assignment.
  const treatmentsByBook = new Map<number, Promise<Map<number, WordLinkTreatment>>>()

  function treatmentsFor(sourceBookId: number): Promise<Map<number, WordLinkTreatment>> {
    const cached = treatmentsByBook.get(sourceBookId)
    if (cached) return cached
    const p = getWordLinkTargetsForBook(sourceBookId)
      .then((targets) => {
        // null = transient failure (DB not ready/swapped): drop the cache entry so a
        // later batch retries; this batch falls back to the splicer's modulo bucket.
        // [] is a real answer (a book whose anchors are all range links) and caches.
        if (targets == null) {
          treatmentsByBook.delete(sourceBookId)
          return new Map<number, WordLinkTreatment>()
        }
        return buildWordLinkTreatments(targets)
      })
      .catch(() => {
        // Transport error (dev service call) — same retry contract as a null result.
        treatmentsByBook.delete(sourceBookId)
        return new Map<number, WordLinkTreatment>()
      })
    treatmentsByBook.set(sourceBookId, p)
    return p
  }

  function scheduleLoad(lineIds: number[]) {
    const pending = lineIds.filter((id) => id > 0 && !requested.has(id))
    if (!pending.length) return

    if (debounceTimer !== null) clearTimeout(debounceTimer)
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      // Mark before the async call so concurrent scroll events don't double-query.
      for (const id of pending) requested.add(id)
      void _loadForLines(pending)
    }, 100)
  }

  async function _loadForLines(lineIds: number[]): Promise<void> {
    try {
      const rows = await getWordLinkAnchorsForLines(lineIds)
      if (!rows.length) return
      // Annotate every anchor with its book's treatment BEFORE exposing it — the
      // splicer renders whatever is on the row, so assignment happens exactly once
      // and never shifts as more lines load.
      const books = [...new Set(rows.map((r) => r.sourceBookId))]
      const maps = new Map(await Promise.all(books.map(async (b) => [b, await treatmentsFor(b)] as const)))
      for (const row of rows) {
        const t = maps.get(row.sourceBookId)?.get(row.targetBookId)
        if (t) {
          row.colorBucket = t.bucket
          if (t.open != null) {
            row.encOpen = t.open
            row.encClose = t.close
          }
        }
      }
      const byLine = new Map<number, WordLinkAnchor[]>()
      for (const row of rows) {
        let list = byLine.get(row.lineId)
        if (!list) byLine.set(row.lineId, (list = []))
        list.push(row)
      }
      for (const [lineId, list] of byLine) anchorsByLine.value.set(lineId, list)
    } catch {
      // DB not ready — un-mark so the lines are retried on the next visible-set change.
      for (const id of lineIds) requested.delete(id)
    }
  }

  if (getVisibleLineIds) watch(getVisibleLineIds, (ids) => scheduleLoad(ids), { immediate: true })

  function getWordLinkAnchorsForLine(lineId: number): WordLinkAnchor[] {
    return anchorsByLine.value.get(lineId) ?? EMPTY
  }

  return { getWordLinkAnchorsForLine, scheduleWordLinkAnchorsLoad: scheduleLoad }
}
