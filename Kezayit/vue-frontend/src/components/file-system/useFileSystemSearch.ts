/**
 * File-system search composable.
 *
 * Two-phase search:
 *
 *   Phase 1 — Instant book match (runs on every keystroke, synchronous)
 *             Filters the in-memory book catalog by the query words.
 *             Results appear immediately with no loading state.
 *             Also cancels any in-flight Phase 2 search so the spinner
 *             never gets stuck when the user keeps typing.
 *
 *   Phase 2 — TOC heuristics fallback (runs debounced, async)
 *             When Phase 1 finds nothing, delegates to fileSystemTocHeuristics
 *             which splits the query into "<book words> <toc words>" and
 *             searches the TOC entries of the matching books.
 *             Shows a loading spinner while the DB fetch is in progress.
 *             Capped at MAX_TOC_CANDIDATE_BOOKS so a broad prefix like "ראש"
 *             doesn't trigger a fetch for hundreds of books.
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { normalizeBookQuery } from '@/utils/bookQueryNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { runTocHeuristics } from './fileSystemTocHeuristics'
import { ensureBookSearchMetadata, filterBooksByWords } from '@/utils/booksCategoryTree'
import type { BookRow } from '@/utils/booksCategoryTree'

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

/**
 * Normalize and split a raw query string into search words.
 * Applies base normalization (lowercase, strip quotes) then book-specific
 * normalization (abbreviation expansion, spelling variants).
 */
function toQueryWords(rawQuery: string): string[] {
  return normalizeBookQuery(normalize(rawQuery.trim()))
    .split(/\s+/)
    .filter((word) => word.length > 0)
}

// ─── Composable ───────────────────────────────────────────────────────────────

export function useFileSystemSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 300)
  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)

  // Monotonically increasing counter — incremented whenever a new search starts
  // OR when the user types new text (Phase 1). Any in-flight Phase 2 async work
  // checks this after every await and aborts if the value has changed.
  let searchGeneration = 0

  // ── Phase 1: instant book match ─────────────────────────────────────────────
  // Runs on every keystroke. Also cancels any in-flight Phase 2 by bumping the
  // generation counter and clearing the spinner — so the user never gets stuck
  // on the loading animation while typing.

  watch(
    searchQuery,
    (rawQuery) => {
      // Cancel any in-flight Phase 2 search immediately
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
        // No book match yet — clear stale results while Phase 2 is pending
        results.value = []
      }
    },
    { immediate: true },
  )

  // ── Phase 2: TOC heuristics fallback ────────────────────────────────────────
  // Runs after the debounce delay. Only fires when Phase 1 found no books.
  // The generation captured at the start of each run is checked after every
  // await — if Phase 1 has already bumped it, the run exits without touching
  // results or the searching flag.

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++searchGeneration
      const words = toQueryWords(rawQuery ?? '')

      if (!words.length) {
        results.value = []
        return
      }

      // Skip TOC search when Phase 1 already found book results
      if (filterBooksByWords(store.allBooks, words).length > 0) return

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
      } finally {
        if (generation === searchGeneration) searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}
