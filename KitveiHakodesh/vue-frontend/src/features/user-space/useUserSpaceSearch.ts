import { computed, ref, watch, type Ref } from 'vue'
import { refDebounced } from '@vueuse/core'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { UserSpaceRoot } from '@/stores/userSpaceStore'
import { userSpaceSearch, type UserSpaceSearchHit, type UserSpaceSearchRoot } from '@/webview-host/bridge'
import { reportCatalogTocSearchResponse, reportSearchIndexStatus } from '@/composables/useSearchIndexStatus'
import type { SearchFsItem } from '@/features/book-catalog/useBookCatalogSearch'
import { withoutHiddenCategories } from '@/features/book-catalog/bookCatalogTree'

/** Poll interval while the catalog index is still building - same as the catalog search. */
const INDEX_NOT_READY_RETRY_MS = 1200

/**
 * UserSpace search: ONE `userSpaceSearch` call searches the catalog under the root categories
 * and the computer under the root folders, filtered inside each index, and returns one list
 * ranked by the catalog search's own rules (files rank as if they were catalog books). The
 * order is the service's and is kept as-is. Same debounce, retry and index-status handling as
 * the catalog search (useBookCatalogSearch).
 */
export function useUserSpaceSearch(searchQuery: Ref<string>, roots: Ref<UserSpaceRoot[]>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 150)
  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)
  const isSearching = computed(() => debouncedQuery.value.trim().length > 1)

  let searchGeneration = 0

  // The whole catalog searches as its top-level shelves: the service filters by category, and
  // every catalog book sits under one of them.
  const searchRoots = computed((): UserSpaceSearchRoot[] =>
    roots.value.flatMap((r): UserSpaceSearchRoot[] => {
      if (r.kind === 'catalogAll') return withoutHiddenCategories(store.ROOT.children).map((c) => ({ categoryId: c.id }))
      return [r.kind === 'catalog' ? { categoryId: r.categoryId } : { folder: r.path }]
    }),
  )

  function toItems(hits: UserSpaceSearchHit[]): SearchFsItem[] {
    const bookById = new Map(store.allBooks.map((b) => [b.id, b]))
    const items: SearchFsItem[] = []
    hits.forEach((hit, i) => {
      if (hit.filePath) {
        const cut = Math.max(hit.filePath.lastIndexOf('\\'), hit.filePath.lastIndexOf('/'))
        items.push({ uid: `p-${hit.filePath}`, kind: 'file', file: { path: hit.filePath, name: hit.filePath.substring(cut + 1), title: hit.title || undefined } })
        return
      }
      const book = bookById.get(hit.bookId)
      if (!book) return // not in the loaded catalog (stale index row)
      if (!hit.tocPath) {
        items.push({ uid: `b-${book.id}`, kind: 'book', book })
        return
      }
      items.push({
        uid: `toc-${hit.bookId}-${i}`,
        kind: 'toc',
        book,
        tocEntryId: 0,
        tocLineIndex: hit.lineIndex >= 0 ? hit.lineIndex : null,
        tocTitle: hit.tocPath.split(' / ').pop() ?? hit.tocPath,
        tocPath: hit.tocPath,
      })
    })
    return items
  }

  // Instant clear so an emptied input doesn't keep showing stale results for the debounce.
  watch(searchQuery, (rawQuery) => {
    if (!rawQuery.trim()) {
      searchGeneration++
      results.value = []
      searching.value = false
    }
  })

  watch(
    [debouncedQuery, searchRoots],
    async ([rawQuery, rootList]) => {
      const generation = ++searchGeneration
      const query = rawQuery.trim()
      if (query.length < 2 || !rootList.length) {
        results.value = []
        searching.value = false
        return
      }
      searching.value = true
      try {
        // Retry while the catalog index is still building - a newer search supersedes the loop.
        for (;;) {
          const res = await userSpaceSearch(query, rootList)
          if (generation !== searchGeneration) return
          reportCatalogTocSearchResponse(res)
          // The file half answers whatever state the catalog index is in, so its hits show
          // at once - on a catalog failure, and while the catalog index is still building.
          if (res.error || res.dbMissing || res.buildFailure) {
            results.value = toItems(res.hits ?? [])
            break
          }
          if (!res.superseded) results.value = toItems(res.hits ?? [])
          if (res.ready && !res.superseded) break
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

  return { results, searching, isSearching }
}
