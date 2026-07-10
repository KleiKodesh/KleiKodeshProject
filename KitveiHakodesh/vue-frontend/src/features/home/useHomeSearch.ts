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
 *
 * Source-priority prefixes (stripped before searching):
 *   HebrewBooks first: היברו, היברובוקס, היברו בוקס, \ (single backslash)
 *   Files first:       מחשב, קובץ, \\ (double backslash)
 *   Default (no prefix): catalog first
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

export type SearchSourcePriority = 'catalog' | 'hebrewbooks' | 'files'

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_QUERY_LENGTH = 2
const DEBOUNCE_MS = 300

// Prefixes that shift which source appears first in the dropdown.
// Matched against the beginning of the normalized query (after stripping spaces).
// The backslash shorthands (\\ for files, \ for HebrewBooks) are checked before
// the Hebrew word prefixes so the longer one (\\) is matched first.
const HEBREWBOOKS_PREFIXES = ['היברו בוקס', 'היברובוקס', 'היברו']
const FILES_PREFIXES = ['מחשב', 'קובץ']
const HEBREWBOOKS_SHORTHAND = '\\'
const FILES_SHORTHAND = '\\\\'

// ─── Prefix detection ────────────────────────────────────────────────────────

interface ParsedQuery {
  priority: SearchSourcePriority
  /** The query after the prefix has been removed, trimmed. */
  effectiveQuery: string
}

function stripPrefixFromQuery(trimmed: string, prefix: string): string {
  const afterPrefix = trimmed.slice(prefix.length)
  // Allow an optional colon (with surrounding spaces) after the prefix
  return afterPrefix.replace(/^\s*:\s*/, '').trim()
}

function parseQueryPrefix(rawQuery: string): ParsedQuery {
  const trimmed = rawQuery.trim()

  // Check shorthand prefixes first — \\ (files) before \ (HebrewBooks) so the
  // longer match wins when the user types two backslashes.
  if (trimmed.startsWith(FILES_SHORTHAND)) {
    return { priority: 'files', effectiveQuery: stripPrefixFromQuery(trimmed, FILES_SHORTHAND) }
  }
  if (trimmed.startsWith(HEBREWBOOKS_SHORTHAND)) {
    return { priority: 'hebrewbooks', effectiveQuery: stripPrefixFromQuery(trimmed, HEBREWBOOKS_SHORTHAND) }
  }

  for (const prefix of HEBREWBOOKS_PREFIXES) {
    if (trimmed.startsWith(prefix)) {
      return { priority: 'hebrewbooks', effectiveQuery: stripPrefixFromQuery(trimmed, prefix) }
    }
  }
  for (const prefix of FILES_PREFIXES) {
    if (trimmed.startsWith(prefix)) {
      return { priority: 'files', effectiveQuery: stripPrefixFromQuery(trimmed, prefix) }
    }
  }
  return { priority: 'catalog', effectiveQuery: trimmed }
}

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
  const sourcePriority = ref<SearchSourcePriority>('catalog')

  const isLoadingHebrewBooks = ref(false)
  const isLoadingFiles = ref(false)

  // When the dropdown has keyboard focus the user is scrolling through results.
  // Applying new results at that point resets scrollTop. Pause updates until
  // the dropdown loses focus.
  const isPaused = ref(false)
  let pendingCatalog: CatalogSearchResult[] | null = null
  let pendingHebrewBooks: HebrewBooksSearchResult[] | null = null
  let pendingFiles: FileSearchResult[] | null = null

  function pause() {
    isPaused.value = true
  }

  function resume() {
    isPaused.value = false
    if (pendingCatalog !== null) { catalogResults.value = pendingCatalog; pendingCatalog = null }
    if (pendingHebrewBooks !== null) { hebrewBooksResults.value = pendingHebrewBooks; pendingHebrewBooks = null }
    if (pendingFiles !== null) { fileResults.value = pendingFiles; pendingFiles = null }
  }

  function setCatalog(results: CatalogSearchResult[]) {
    if (isPaused.value) { pendingCatalog = results } else { catalogResults.value = results }
  }
  function setHebrewBooks(results: HebrewBooksSearchResult[]) {
    if (isPaused.value) { pendingHebrewBooks = results } else { hebrewBooksResults.value = results }
  }
  function setFiles(results: FileSearchResult[]) {
    if (isPaused.value) { pendingFiles = results } else { fileResults.value = results }
  }

  // Track async generation so stale responses are discarded
  let asyncGeneration = 0

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)

  // ── Catalog search — instant on every keystroke ───────────────────────────

  watch(
    searchQuery,
    (rawQuery) => {
      const { priority, effectiveQuery } = parseQueryPrefix(rawQuery ?? '')
      sourcePriority.value = priority

      if (effectiveQuery.length < MIN_QUERY_LENGTH || !dbReady.value) {
        setCatalog([])
        return
      }
      const words = toQueryWords(effectiveQuery)
      if (!words.length) {
        setCatalog([])
        return
      }
      setCatalog(filterBooksByWords(booksDataStore.allBooks, words).slice(0, 50).map((book) => ({
        source: 'catalog' as const,
        book,
      })))
    },
    { immediate: true },
  )

  // ── HebrewBooks + file search — debounced ─────────────────────────────────

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++asyncGeneration
      const { effectiveQuery } = parseQueryPrefix(rawQuery ?? '')

      if (effectiveQuery.length < MIN_QUERY_LENGTH || !isHosted) {
        setHebrewBooks([])
        setFiles([])
        isLoadingHebrewBooks.value = false
        isLoadingFiles.value = false
        return
      }

      isLoadingHebrewBooks.value = true
      isLoadingFiles.value = true

      const localFolder = settingsStore.hebrewBooksLocalFolder || undefined

      const hbPromise = searchHbCatalog(effectiveQuery, localFolder, 50)
        .then((books) => {
          if (generation !== asyncGeneration) return
          setHebrewBooks(books.map((book) => ({ source: 'hebrewbooks' as const, book })))
        })
        .catch(() => {
          if (generation !== asyncGeneration) return
          setHebrewBooks([])
        })
        .finally(() => {
          if (generation === asyncGeneration) isLoadingHebrewBooks.value = false
        })

      const filePromise = fileSystemSearch(effectiveQuery, 50)
        .then((response) => {
          if (generation !== asyncGeneration) return
          if (response.error || !response.results) {
            setFiles([])
            return
          }
          setFiles(response.results.map((item) => ({
            source: 'files' as const,
            fileName: item.fileName,
            fullPath: item.path ? `${item.path}\\${item.fileName}` : item.fileName,
          })))
        })
        .catch(() => {
          if (generation !== asyncGeneration) return
          setFiles([])
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
    pendingCatalog = null
    pendingHebrewBooks = null
    pendingFiles = null
    catalogResults.value = []
    hebrewBooksResults.value = []
    fileResults.value = []
    isLoadingHebrewBooks.value = false
    isLoadingFiles.value = false
    sourcePriority.value = 'catalog'
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
    sourcePriority,
    isLoadingHebrewBooks,
    isLoadingFiles,
    hasAnyResults,
    isLoadingAny,
    clearResults,
    pause,
    resume,
  }
}
