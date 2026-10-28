/**
 * A full-text hit's REFERENCE — its book title and TOC path — resolved on demand.
 *
 * Hits carry only numbers (see FullTextSearchResult): the title comes from the catalog the
 * books store already holds, keyed by bookId; the TOC path is fetched by line id
 * (getTocPathsForLines) for the rows the windowed scroller actually renders and kept in a
 * small LRU — the same shape as the on-demand snippets in fullTextSearchSnippets.ts. The
 * search itself never reads TOC data.
 */
import { shallowRef } from 'vue'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { getTocPathsForLines } from '@/webview-host/seforimApi'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/** The hit's book title from the catalog; '' for a book the catalog does not list. */
export function bookTitleOf(hit: FullTextSearchResult): string {
  return useBooksDataStore().allBooksMap.get(hit.bookId)?.title ?? ''
}

// ── On-demand TOC path cache ─────────────────────────────────────────────────────────

/** Paths held, by line id. A screenful of rows plus lookahead is far below this. */
const TOC_PATH_CAP = 3000
/** Ids per request; the service binds one parameter per id, under SQLite's 999 limit. */
const FETCH_BATCH = 500

const tocPaths = new Map<number, string>()
const inFlight = new Set<number>()

/** Bumped whenever fetched paths land — the reactive dependency of tocPathOf. */
const referencesVersion = shallowRef(0)

function touch<K, V>(map: Map<K, V>, key: K, value: V, cap: number) {
  map.delete(key)
  map.set(key, value)
  if (map.size > cap) map.delete(map.keys().next().value as K)
}

/**
 * The hit's TOC path, '' when it has none, or undefined while it is not fetched yet.
 * Reactive: re-evaluates when requested paths arrive.
 */
export function tocPathOf(hit: FullTextSearchResult): string | undefined {
  void referencesVersion.value
  const cached = tocPaths.get(hit.lineId)
  if (cached !== undefined) touch(tocPaths, hit.lineId, cached, TOC_PATH_CAP)
  return cached
}

/**
 * Fetches the TOC paths the given hits need and are missing — one request per
 * FETCH_BATCH ids, deduplicated against requests still in flight. The caller passes the rows the
 * windowed scroller is about to mount, on every range change.
 */
export function requestReferences(hits: readonly FullTextSearchResult[]): void {
  const lineIds: number[] = []
  for (const hit of hits) {
    if (tocPaths.has(hit.lineId) || inFlight.has(hit.lineId)) continue
    inFlight.add(hit.lineId)
    lineIds.push(hit.lineId)
  }
  for (let i = 0; i < lineIds.length; i += FETCH_BATCH) {
    const batch = lineIds.slice(i, i + FETCH_BATCH)
    getTocPathsForLines(batch)
      .then((paths) => {
        // A line with no TOC entry is absent from the answer: cache it as '' so it is not asked again.
        for (const lineId of batch) touch(tocPaths, lineId, paths.get(lineId) ?? '', TOC_PATH_CAP)
      })
      .catch((err) => console.error('[fullTextSearchReferences] TOC path fetch failed:', err))
      .finally(() => {
        for (const lineId of batch) inFlight.delete(lineId)
        referencesVersion.value++
      })
  }
}
