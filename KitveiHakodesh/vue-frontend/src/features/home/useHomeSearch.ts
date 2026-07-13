/**
 * Unified home-page search across three sources:
 *   1. Book catalog — title-only, instant (in-memory inverted index).
 *      When the title search finds nothing, falls back to the TOC heuristics
 *      (debounced) exactly like the catalog page: "בראשית פרק ד" splits into
 *      book="בראשית" and toc="פרק ד" and searches the TOC entries of the
 *      matching books. When the title search DID find books but the query
 *      contains a structural TOC keyword ("משנה תורה הלכות שבת"), the TOC
 *      results are additionally shown below the book results. Results share
 *      the catalog page's IDB LRU cache.
 *   2. HebrewBooks  — async, only when isHosted
 *   3. Document Locator (file system) — async, only when isHosted
 *
 * Each source resolves independently and writes to its own ref so the
 * dropdown can render partial results as they arrive.
 *
 * Min query length: 2 chars. Debounce: 300ms for the async sources.
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
import {
  runTocHeuristics,
  runTocKeywordHeuristics,
  splitQueryAtTocKeyword,
} from '@/features/book-catalog/bookCatalogSearchTocHeuristics'
import { isTocKeyword } from '@/features/book-catalog/bookCatalogTocKeywords'
import {
  getCatalogTocCache,
  setCatalogTocCache,
} from '@/features/book-catalog/bookCatalogTocSearchCache'
import { searchHbCatalog, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { fileSystemSearch } from '@/webview-host/bridge'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { isHosted, dbReady } from '@/webview-host/seforimDb'
import type { BookRow } from '@/features/book-catalog/bookCatalogTree'
import type { TocFsItem } from '@/features/book-catalog/useBookCatalogSearch'

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
const MAX_RESULTS_PER_SOURCE = 50

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
  const catalogTocResults = ref<TocFsItem[]>([])
  const hebrewBooksResults = ref<HebrewBooksSearchResult[]>([])
  const fileResults = ref<FileSearchResult[]>([])
  const sourcePriority = ref<SearchSourcePriority>('catalog')

  const isLoadingCatalogToc = ref(false)
  const isLoadingHebrewBooks = ref(false)
  const isLoadingFiles = ref(false)

  // When the dropdown has keyboard focus the user is scrolling through results.
  // Applying new results at that point resets scrollTop. Pause updates until
  // the dropdown loses focus.
  const isPaused = ref(false)
  let pendingCatalog: CatalogSearchResult[] | null = null
  let pendingCatalogToc: TocFsItem[] | null = null
  let pendingHebrewBooks: HebrewBooksSearchResult[] | null = null
  let pendingFiles: FileSearchResult[] | null = null

  function pause() {
    isPaused.value = true
  }

  function resume() {
    isPaused.value = false
    if (pendingCatalog !== null) { catalogResults.value = pendingCatalog; pendingCatalog = null }
    if (pendingCatalogToc !== null) { catalogTocResults.value = pendingCatalogToc; pendingCatalogToc = null }
    if (pendingHebrewBooks !== null) { hebrewBooksResults.value = pendingHebrewBooks; pendingHebrewBooks = null }
    if (pendingFiles !== null) { fileResults.value = pendingFiles; pendingFiles = null }
  }

  function setCatalog(results: CatalogSearchResult[]) {
    if (isPaused.value) { pendingCatalog = results } else { catalogResults.value = results }
  }
  function setCatalogToc(results: TocFsItem[]) {
    if (isPaused.value) { pendingCatalogToc = results } else { catalogTocResults.value = results }
  }
  function setHebrewBooks(results: HebrewBooksSearchResult[]) {
    if (isPaused.value) { pendingHebrewBooks = results } else { hebrewBooksResults.value = results }
  }
  function setFiles(results: FileSearchResult[]) {
    if (isPaused.value) { pendingFiles = results } else { fileResults.value = results }
  }

  // Track async generation so stale responses are discarded
  let asyncGeneration = 0
  // Separate counter for the TOC heuristics fallback — the HB/files watcher
  // increments asyncGeneration on the same debounced query, so sharing one
  // counter would make each watcher cancel the other.
  let tocGeneration = 0

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)

  // ── Catalog search — instant on every keystroke ───────────────────────────

  watch(
    searchQuery,
    (rawQuery) => {
      const { priority, effectiveQuery } = parseQueryPrefix(rawQuery ?? '')
      sourcePriority.value = priority

      // Cancel any in-flight TOC heuristics — the query has changed
      tocGeneration++

      if (effectiveQuery.length < MIN_QUERY_LENGTH || !dbReady.value) {
        setCatalog([])
        setCatalogToc([])
        isLoadingCatalogToc.value = false
        return
      }
      const words = toQueryWords(effectiveQuery)
      if (!words.length) {
        setCatalog([])
        setCatalogToc([])
        isLoadingCatalogToc.value = false
        return
      }
      const matched = filterBooksByWords(booksDataStore.allBooks, words)
      setCatalog(matched.slice(0, MAX_RESULTS_PER_SOURCE).map((book) => ({
        source: 'catalog' as const,
        book,
      })))
      // Title search found books — the TOC fallback doesn't apply
      if (matched.length > 0) {
        setCatalogToc([])
        isLoadingCatalogToc.value = false
      }
    },
    { immediate: true },
  )

  // ── Catalog TOC heuristics — debounced, two triggers ────────────────────────
  //
  // a) Fallback: the title search found nothing — same flow as
  //    useBookCatalogSearch Phase 2 (longest book-matching prefix split).
  // b) Keyword (additive): the title search DID find books but the query
  //    contains a structural TOC keyword — TOC results are shown below the
  //    book results in the dropdown's catalog section.
  //
  // Both check the shared IDB result cache first, otherwise run the heuristics
  // pipeline against the DB. A generation counter discards stale responses;
  // the instant watcher above bumps it on every keystroke.

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++tocGeneration
      const { effectiveQuery } = parseQueryPrefix(rawQuery ?? '')

      if (effectiveQuery.length < MIN_QUERY_LENGTH || !dbReady.value) return
      const words = toQueryWords(effectiveQuery)
      if (!words.length) return

      const filterBooks = (bookWords: string[]) =>
        filterBooksByWords(booksDataStore.allBooks, bookWords)

      // Keyword trigger only applies when the title search found books AND the
      // query has a keyword split; pure book queries are done — Phase 1
      // already rendered them (and the instant watcher cleared old TOC items).
      const isKeywordTrigger = filterBooks(words).length > 0
      if (
        isKeywordTrigger &&
        !splitQueryAtTocKeyword(words, isTocKeyword, (ws) => filterBooks(ws).length > 0)
      ) {
        return
      }

      // Check the shared disk cache before hitting the DB
      const normalizedQuery = words.join(' ')
      const cached = await getCatalogTocCache(normalizedQuery)
      if (generation !== tocGeneration) return
      if (cached) {
        setCatalogToc(cached.items.slice(0, MAX_RESULTS_PER_SOURCE))
        isLoadingCatalogToc.value = false
        return
      }

      isLoadingCatalogToc.value = true

      try {
        const { items } = isKeywordTrigger
          ? await runTocKeywordHeuristics(
              words,
              filterBooks,
              isTocKeyword,
              () => generation !== tocGeneration,
            )
          : await runTocHeuristics(words, filterBooks, () => generation !== tocGeneration)

        if (generation !== tocGeneration) return

        setCatalogToc(items.slice(0, MAX_RESULTS_PER_SOURCE))

        // Persist the full result set to the shared cache (fire-and-forget)
        if (items.length > 0) setCatalogTocCache(normalizedQuery, items)
      } catch {
        if (generation === tocGeneration) setCatalogToc([])
      } finally {
        if (generation === tocGeneration) isLoadingCatalogToc.value = false
      }
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
    tocGeneration++
    pendingCatalog = null
    pendingCatalogToc = null
    pendingHebrewBooks = null
    pendingFiles = null
    catalogResults.value = []
    catalogTocResults.value = []
    hebrewBooksResults.value = []
    fileResults.value = []
    isLoadingCatalogToc.value = false
    isLoadingHebrewBooks.value = false
    isLoadingFiles.value = false
    sourcePriority.value = 'catalog'
  }

  const hasAnyResults = () =>
    catalogResults.value.length > 0 ||
    catalogTocResults.value.length > 0 ||
    hebrewBooksResults.value.length > 0 ||
    fileResults.value.length > 0

  const isLoadingAny = () =>
    isLoadingCatalogToc.value || isLoadingHebrewBooks.value || isLoadingFiles.value

  return {
    catalogResults,
    catalogTocResults,
    hebrewBooksResults,
    fileResults,
    sourcePriority,
    isLoadingCatalogToc,
    isLoadingHebrewBooks,
    isLoadingFiles,
    hasAnyResults,
    isLoadingAny,
    clearResults,
    pause,
    resume,
  }
}
