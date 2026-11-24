/**
 * Book-title search: book ids matching a query by title, catalog path or author, best first.
 * Runs in the catalog index (Core SeforimDbCatalogIndex.SearchTitles, op `catalogTitleSearch`)
 * — the same normalizer, abbreviations and spelling variants as the catalog TOC search.
 */

import { catalogTitleSearch } from '@/webview-host/bridge'
import { reportCatalogTocSearchResponse, reportSearchIndexStatus } from '@/composables/useSearchIndexStatus'

const INDEX_NOT_READY_RETRY_MS = 1200
const SUPERSEDED_RETRY_MS = 150

/**
 * Book ids for `query`, in rank order. Waits while the index is still building.
 * Resolves null when `isStale()` turns true (a newer request took over) or when the index
 * can never answer (no seforim DB, failed build, service error) — the shared index status
 * tells the user why.
 */
export async function searchBookTitleIds(
  query: string,
  isStale: () => boolean = () => false,
): Promise<number[] | null> {
  try {
    for (;;) {
      const res = await catalogTitleSearch(query)
      if (isStale()) return null
      reportCatalogTocSearchResponse(res)
      if (res.error || res.dbMissing || res.buildFailure) return null
      if (res.ready && !res.superseded) return res.bookIds ?? []
      await new Promise((resolve) => setTimeout(resolve, res.superseded ? SUPERSEDED_RETRY_MS : INDEX_NOT_READY_RETRY_MS))
      if (isStale()) return null
    }
  } catch {
    if (!isStale()) reportSearchIndexStatus('catalogToc', { kind: 'unavailable', reason: 'serviceError' })
    return null
  }
}
