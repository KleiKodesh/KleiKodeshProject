/**
 * Unified home-page search across three sources:
 *   1. Book catalog — title-only, instant (in-memory inverted index)
 *   2. HebrewBooks  — async, only when isHosted
 *   3. Document Locator (file system) — async, only when isHosted
 *
 * Each source resolves independently and writes to its own ref so the
 * dropdown can render partial results as they arrive.
 *
 * Min query length: 2 chars. Debounce: 300ms for the two async sources.
 * Each source is capped at MAX_RESULTS_PER_SOURCE items in the dropdown.
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from '@/features/book-catalog/bookCatalogSearchNormalizer'
import { filterBooksByWords } from '@/features/book-catalog/bookCatalogSearch'
import { searchHbCatalog, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { fileSystemSearch } from '@/webview-host/bridge'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { isHosted, dbReady } from '@/webview-host/seforimDb'
import type { BookRow } from '@/features/book-catalog/bookCatalogTree'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface CatalogSearchResult {
  source: 'catalog'
  book: BookRow
}

export interface HebrewBooksSearchResult {
  source: 'hebrewbooks'
  book: HebrewBook
}

export interface FileSearchResult {
  source: 'files'
  fileName: string
  fullPath: string
}

export type HomeSearchResult = CatalogSearchResult | HebrewBooksSearchResult | FileSearchResult

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_QUERY_LENGTH = 2
const DEBOUNCE_MS = 300

// ─── Query normalization ──────────────────────────────────────────────────────

function toQueryWords(rawQuery: string): string[] {
  return normalizeBookPath(normalize(rawQuery.trim()))
    .split(/\s+/)
    .filter((word) => word.length > 0)
}

// ─── Composable ───────────────────────────────────────────────────────────────

export function useHomeSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const booksDataStore = useBooksDataStore()
  const settingsStore = useSettingsStore()

  const catalogResults = ref<CatalogSearchResult[]>([])
  const hebrewBooksResults = ref<HebrewBooksSearchResult[]>([])
  const fileResults = ref<FileSearchResult[]>([])

  const isLoadingHebrewBooks = ref(false)
  const isLoadingFiles = ref(false)

  // Track async generation so stale responses are discarded
  let asyncGeneration = 0

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)

  // ── Catalog search — instant on every keystroke ───────────────────────────

  watch(
    searchQuery,
    (rawQuery) => {
      const trimmed = (rawQuery ?? '').trim()
      if (trimmed.length < MIN_QUERY_LENGTH || !dbReady.value) {
        catalogResults.value = []
        return
      }
      const words = toQueryWords(trimmed)
      if (!words.length) {
        catalogResults.value = []
        return
      }
      catalogResults.value = filterBooksByWords(booksDataStore.allBooks, words).slice(0, 50).map((book) => ({
        source: 'catalog' as const,
        book,
      }))
    },
    { immediate: true },
  )

  // ── HebrewBooks + file search — debounced ─────────────────────────────────

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++asyncGeneration
      const trimmed = (rawQuery ?? '').trim()

      if (trimmed.length < MIN_QUERY_LENGTH || !isHosted) {
        hebrewBooksResults.value = []
        fileResults.value = []
        isLoadingHebrewBooks.value = false
        isLoadingFiles.value = false
        return
      }

      // Fire both in parallel
      isLoadingHebrewBooks.value = true
      isLoadingFiles.value = true

      const localFolder = settingsStore.hebrewBooksLocalFolder || undefined

      // 50-result cap for the home search dropdown — passed through to the SQLite
      // LIMIT clause in HebrewBooksDb.Search() so SQLite stops scanning early.
      // The full HebrewBooks page passes no limit (defaults to 200 server-side).
      const hbPromise = searchHbCatalog(trimmed, localFolder, 50)
        .then((books) => {
          if (generation !== asyncGeneration) return
          hebrewBooksResults.value = books.map((book) => ({
            source: 'hebrewbooks' as const,
            book,
          }))
        })
        .catch(() => {
          if (generation !== asyncGeneration) return
          hebrewBooksResults.value = []
        })
        .finally(() => {
          if (generation === asyncGeneration) isLoadingHebrewBooks.value = false
        })

      // 50-result cap for the home search dropdown — this limit is passed to the
      // DocumentLocator service and enforced by Lucene server-side (early exit),
      // not a cosmetic slice. The full file-search page uses 5000.
      const filePromise = fileSystemSearch(trimmed, 50)
        .then((response) => {
          if (generation !== asyncGeneration) return
          if (response.error || !response.results) {
            fileResults.value = []
            return
          }
          fileResults.value = response.results.map((item) => ({
            source: 'files' as const,
            fileName: item.fileName,
            fullPath: item.path ? `${item.path}\\${item.fileName}` : item.fileName,
          }))
        })
        .catch(() => {
          if (generation !== asyncGeneration) return
          fileResults.value = []
        })
        .finally(() => {
          if (generation === asyncGeneration) isLoadingFiles.value = false
        })

      await Promise.allSettled([hbPromise, filePromise])
    },
    { immediate: true },
  )

  function clearResults() {
    asyncGeneration++
    catalogResults.value = []
    hebrewBooksResults.value = []
    fileResults.value = []
    isLoadingHebrewBooks.value = false
    isLoadingFiles.value = false
  }

  const hasAnyResults = () =>
    catalogResults.value.length > 0 ||
    hebrewBooksResults.value.length > 0 ||
    fileResults.value.length > 0

  const isLoadingAny = () => isLoadingHebrewBooks.value || isLoadingFiles.value

  return {
    catalogResults,
    hebrewBooksResults,
    fileResults,
    isLoadingHebrewBooks,
    isLoadingFiles,
    hasAnyResults,
    isLoadingAny,
    clearResults,
  }
}
