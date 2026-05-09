/**
 * File-system search composable.
 *
 * Two-phase search:
 *
 *   Phase 1 — Instant book match (runs on every keystroke, synchronous)
 *             Filters the in-memory book catalog by the query words.
 *             Results appear immediately with no loading state.
 *             Also cancels any in-flight Phase 2 search.
 *
 *   Phase 2 — TOC heuristics fallback (debounced 300ms, async)
 *             When Phase 1 finds nothing, delegates to bookCatalogTocHeuristics
 *             which splits the query into "<book words> <toc words>" and
 *             searches the TOC entries of the matching books.
 *             Results are cached in app-catalog-toc-cache IDB (LRU, 25 entries)
 *             so repeated queries skip the DB round-trips entirely.
 *             Shows a loading spinner only on a cache miss while the DB fetch
 *             is in progress. Capped at MAX_TOC_CANDIDATE_BOOKS so a broad
 *             prefix like "ראש" doesn't trigger a fetch for hundreds of books.
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from './bookCatalogSearchNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { runTocHeuristics } from './bookCatalogSearchTocHeuristics'
import { filterBooksByWords } from './bookCatalogSearch'
import { getCatalogTocCache, setCatalogTocCache } from './bookCatalogTocSearchCache'
import type { BookRow } from './bookCatalogTree'

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

// ─── Query normalization ──────────────────────────────────────────────────────

function toQueryWords(rawQuery: string): string[] {
  return normalizeBookPath(normalize(rawQuery.trim()))
    .split(/\s+/)
    .filter((word) => word.length > 0)
}

// ─── Composable ───────────────────────────────────────────────────────────────

export function useBookCatalogSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 300)
  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)

  let searchGeneration = 0

  // ── Phase 1: instant book match ─────────────────────────────────────────────

  watch(
    searchQuery,
    (rawQuery) => {
      searchGeneration++
      searching.value = false

      const words = toQueryWords(rawQuery ?? '')
      if (!words.length) {
        results.value = []
        return
      }

      const matchedBooks = filterBooksByWords(store.allBooks, words)
      if (matchedBooks.length) {
        results.value = matchedBooks.map((book) => ({
          uid: `b-${book.id}`,
          kind: 'book' as const,
          book,
        }))
      } else {
        results.value = []
      }
    },
    { immediate: true },
  )

  // ── Phase 2: TOC heuristics fallback ────────────────────────────────────────

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++searchGeneration
      const words = toQueryWords(rawQuery ?? '')

      if (!words.length) {
        results.value = []
        return
      }

      if (filterBooksByWords(store.allBooks, words).length > 0) return

      // Check the disk cache before hitting the DB
      const normalizedQuery = words.join(' ')
      const cached = await getCatalogTocCache(normalizedQuery)
      if (generation !== searchGeneration) return
      if (cached) {
        results.value = cached.items
        return
      }

      searching.value = true
      results.value = []

      try {
        const { items } = await runTocHeuristics(
          words,
          (bookWords) => filterBooksByWords(store.allBooks, bookWords),
          () => generation !== searchGeneration,
        )

        if (generation !== searchGeneration) return

        results.value = items

        // Persist to disk cache (fire-and-forget — UI is already updated)
        if (items.length > 0) setCatalogTocCache(normalizedQuery, items)
      } finally {
        if (generation === searchGeneration) searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}
