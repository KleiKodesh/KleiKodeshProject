/**
 * Catalog "file-system" search.
 *
 * One path, both modes: a single call to the Lucene catalog TOC index (`catalogTocSearch`)
 * answers the whole query — book-title docs and full-TOC-path docs are searched together and
 * come back ranked. Both modes reach it the same way: `serviceCall` to the KitveiHakodesh
 * service, which owns the index (Core SeforimDbCatalogIndex).
 *
 * Ordering is the index's own and is preserved as-is: literal matches first, then TOC level,
 * then catalog tree order. Book hits are simply the level-0 docs, so they lead. Results are
 * never capped.
 *
 * Hosted formerly ran a separate two-phase pipeline (in-memory book match + TOC keyword
 * heuristics) because it had no index to query, which meant the same query ranked differently
 * in the two modes. That pipeline and its in-memory book index are deleted; book-title-only
 * lookups use the same index's title search (bookTitleSearch.ts).
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { catalogTocSearch } from '@/webview-host/bridge'
import { reportCatalogTocSearchResponse, reportSearchIndexStatus } from '@/composables/useSearchIndexStatus'
import type { BookRow } from '@/webview-host/queries.types'
// ─── Public types ─────────────────────────────────────────────────────────────

export type BookFsItem = { uid: string; kind: 'book'; book: BookRow }
export type TocFsItem = {
  uid: string
  kind: 'toc'
  book: BookRow
  tocEntryId: number
  tocLineIndex: number | null
  tocTitle: string
  tocPath: string
}
export type SearchFsItem = BookFsItem | TocFsItem

// ─── Lucene service search (dev) ──────────────────────────────────────────────

/** One `catalogTocSearch` hit on the wire (serviceClient camelCases the msgpack keys). */
type CatalogTocServiceHit = {
  bookId: number
  lineIndex: number // -1 = no resolved line
  /** " / "-joined TOC segments WITHOUT the book title (prefixed client-side); "" for a title hit. */
  tocPath: string
  /** 0 = book-title hit, 1+ = TOC depth. */
  level: number
  treeOrder: number
}

type CatalogTocServiceResult = {
  ready: boolean
  results: CatalogTocServiceHit[]
  /** True when a newer search cancelled this one server-side — discard and retry. */
  superseded?: boolean
  error?: string | null
  dbMissing?: boolean
  builtBooks?: number
  totalBooks?: number
  buildFailure?: string | null
}

/** Poll interval while the service is still building the index (first start after a
 * seforim-DB change) — the search stays in its loading state until ready. */
const INDEX_NOT_READY_RETRY_MS = 1200

/**
 * Dev path: the service's Lucene index over book titles + full TOC paths answers the
 * whole query in one call. The service's deterministic order (TOC level, then catalog
 * tree order) is preserved as-is — book hits are simply the level-0 docs, so they lead.
 */
function useLuceneCatalogSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 150)
  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)

  let searchGeneration = 0

  const toItems = (hits: CatalogTocServiceHit[]): SearchFsItem[] => {
    const bookById = new Map(store.allBooks.map((b) => [b.id, b]))
    const items: SearchFsItem[] = []
    for (const hit of hits) {
      const book = bookById.get(hit.bookId)
      if (!book) continue // book not in the loaded catalog (e.g. stale index row)
      if (hit.level === 0) {
        items.push({ uid: `b-${book.id}`, kind: 'book', book })
      } else {
        // The UI prepends the book title itself. Navigation is line-based (tocEntryId
        // is no longer carried by the index).
        const tocPath = hit.tocPath
        items.push({
          uid: `toc-${hit.bookId}-${hit.treeOrder}`,
          kind: 'toc',
          book,
          tocEntryId: 0,
          tocLineIndex: hit.lineIndex >= 0 ? hit.lineIndex : null,
          tocTitle: tocPath.split(' / ').pop() ?? tocPath,
          tocPath,
        })
      }
    }
    return items
  }

  // Instant clear so an emptied input doesn't keep showing stale results for 150ms.
  watch(searchQuery, (rawQuery) => {
    if (!rawQuery?.trim()) {
      searchGeneration++
      results.value = []
      searching.value = false
    }
  })

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++searchGeneration
      const query = (rawQuery ?? '').trim()
      if (!query) {
        results.value = []
        searching.value = false
        return
      }

      searching.value = true
      try {
        // Retry while the index is still building — a newer search supersedes the loop.
        for (;;) {
          const res = await catalogTocSearch(query)
          if (generation !== searchGeneration) return
          reportCatalogTocSearchResponse(res)
          // Terminal failure (index unreadable, or no seforim DB) — stop polling; the
          // shared index status tells the user why.
          if (res.error || res.dbMissing || res.buildFailure) {
            results.value = []
            break
          }
          if (res.ready && !res.superseded) {
            results.value = toItems(res.results ?? [])
            break
          }
          // superseded (requests raced on the pipe) → re-issue promptly; not-ready
          // (index still building) → poll slowly.
          await new Promise((resolve) => setTimeout(resolve, res.superseded ? 150 : INDEX_NOT_READY_RETRY_MS))
          if (generation !== searchGeneration) return
        }
      } catch {
        if (generation === searchGeneration) {
          reportSearchIndexStatus('catalogToc', { kind: 'unavailable', reason: 'serviceError' })
          results.value = []
        }
      } finally {
        if (generation === searchGeneration) searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}

// ─── Composable ───────────────────────────────────────────────────────────────

export function useBookCatalogSearch(searchQuery: ReturnType<typeof ref<string>>) {
  // Both modes run the SAME Lucene index, reached the same way: `serviceCall` to the
  // KitveiHakodesh service, which owns it.
  //
  // Hosted used to fall through to a manual two-phase pipeline here — an in-memory book match
  // plus TOC keyword heuristics — because it had no index to query. That made the same query
  // rank differently depending on which mode the app ran in. It is deleted rather than kept
  // behind a flag: there is no longer a code path that can reach it. The heuristics modules
  // it depended on are deleted too.
  return useLuceneCatalogSearch(searchQuery)
}
