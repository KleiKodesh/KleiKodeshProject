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
 *   Phase 2 — TOC heuristics (debounced 300ms, async), two triggers:
 *             a) Fallback — when Phase 1 finds nothing, splits the query into
 *                "<book words> <toc words>" (longest book-matching prefix) and
 *                searches the TOC entries of the matching books.
 *             b) Keyword (additive) — when Phase 1 DID find books but the query
 *                contains a structural TOC keyword (פרק, סימן, הלכות …), splits
 *                at the keyword and APPENDS the TOC results below the books.
 *             Results are cached in app-catalog-toc-cache IDB (LRU, 25 entries)
 *             so repeated queries skip the DB round-trips entirely.
 *             Shows a loading spinner only on a fallback-trigger cache miss
 *             while the DB fetch is in progress. Capped at
 *             MAX_TOC_CANDIDATE_BOOKS so a broad prefix like "ראש" doesn't
 *             trigger a fetch for hundreds of books.
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from './bookCatalogSearchNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import {
  runTocHeuristics,
  runTocKeywordHeuristics,
  splitQueryAtTocKeyword,
} from './bookCatalogSearchTocHeuristics'
import { isTocKeyword } from './bookCatalogTocKeywords'
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

      const filterBooks = (bookWords: string[]) => filterBooksByWords(store.allBooks, bookWords)
      const matchedBooks = filterBooks(words)

      // Keyword trigger (additive): Phase 1 found books, but the query contains
      // a structural TOC keyword with a book-matching prefix before it — append
      // TOC results below the book results. Pure book queries (no valid keyword
      // split) are done right here — Phase 1 already rendered them.
      const isKeywordTrigger = matchedBooks.length > 0
      if (
        isKeywordTrigger &&
        !splitQueryAtTocKeyword(words, isTocKeyword, (ws) => filterBooks(ws).length > 0)
      ) {
        return
      }

      const bookItems: SearchFsItem[] = isKeywordTrigger
        ? matchedBooks.map((book) => ({ uid: `b-${book.id}`, kind: 'book' as const, book }))
        : []

      const applyItems = (tocItems: TocFsItem[]) => {
        results.value = isKeywordTrigger ? [...bookItems, ...tocItems] : tocItems
      }

      // Check the disk cache before hitting the DB
      const normalizedQuery = words.join(' ')
      const cached = await getCatalogTocCache(normalizedQuery)
      if (generation !== searchGeneration) return
      if (cached) {
        applyItems(cached.items)
        return
      }

      // Only the fallback trigger blanks the list while searching — the keyword
      // trigger keeps the already-visible book results on screen.
      if (!isKeywordTrigger) {
        searching.value = true
        results.value = []
      }

      try {
        const { items } = isKeywordTrigger
          ? await runTocKeywordHeuristics(
              words,
              filterBooks,
              isTocKeyword,
              () => generation !== searchGeneration,
            )
          : await runTocHeuristics(words, filterBooks, () => generation !== searchGeneration)

        if (generation !== searchGeneration) return

        // Keyword trigger with no keyword split (or no items): leave the book
        // results exactly as Phase 1 rendered them.
        if (isKeywordTrigger && !items.length) return

        applyItems(items)

        // Persist to disk cache (fire-and-forget — UI is already updated)
        if (items.length > 0) setCatalogTocCache(normalizedQuery, items)
      } catch {
        if (generation === searchGeneration && !isKeywordTrigger) results.value = []
      } finally {
        if (generation === searchGeneration && !isKeywordTrigger) searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}
