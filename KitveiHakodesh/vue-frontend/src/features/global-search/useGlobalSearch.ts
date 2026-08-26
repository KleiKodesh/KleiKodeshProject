/**
 * Unified quick search across three sources, shared by the home page's hero
 * search bar and the title bar's AddressBar:
 *   1. Book catalog — the Lucene catalog TOC index, via `catalogTocSearch`.
 *      The SAME engine and the SAME single call the catalog page makes (see
 *      useBookCatalogSearch): book-title docs and full-TOC-path docs are
 *      searched together and come back ranked. Level-0 hits become the book
 *      results, level 1+ the TOC results shown below them.
 *      Debounced at CATALOG_DEBOUNCE_MS — shorter than the other two sources.
 *
 *      This used to be an instant in-memory inverted-index match over titles,
 *      with a TOC-heuristics fallback. That index applied only a two-rule
 *      frontend normalizer, so it could not resolve the abbreviations the
 *      backend expands (ט"ז → its full title) — the same query answered
 *      differently here and on the catalog page. Routing both through one
 *      engine is what keeps them consistent; do not reintroduce a parallel
 *      frontend matcher. The old pieces are still exported from
 *      bookCatalogSearch / bookCatalogSearchTocHeuristics if ever needed.
 *   2. Document Locator (file system) — async; hosted via C#, dev via the service
 *   3. HebrewBooks  — async; hosted via C#, dev via the service
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
 *   The backslash shorthands also work as a suffix: "query\" and "query\\".
 *   Default (no prefix): catalog first
 *
 * "תוספים" also puts files first, but it is a term rather than a prefix — see
 * features/local-file-search/otzariaAddins.ts, which owns the addin rules the
 * file-search page and this composable share.
 */

import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { searchHbCatalog, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { normalizeAddinQuery, queryTargetsAddins } from '@/features/local-file-search/otzariaAddins'
import { catalogTocSearch, fileSystemSearch } from '@/webview-host/bridge'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useSettingsStore } from '@/stores/settingsStore'
import type { BookRow } from '@/webview-host/queries.types'
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
  /** Non-empty only for Otzaria addin entry points. Value is "תוסף אוצריא: {name}". */
  addinName: string
}

export type GlobalSearchResult = CatalogSearchResult | HebrewBooksSearchResult | FileSearchResult

export type SearchSourcePriority = 'catalog' | 'hebrewbooks' | 'files'

/** One `catalogTocSearch` hit on the wire (serviceClient camelCases the msgpack keys).
 *  Mirrors the shape in useBookCatalogSearch — both read the same index. */
interface CatalogTocServiceHit {
  bookId: number
  /** -1 = no resolved line. */
  lineIndex: number
  /** Display path: book title, then " / "-joined TOC segments. */
  fullTocPath: string
  /** 0 = book-title hit, 1+ = TOC depth. */
  level: number
  treeOrder: number
}

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_QUERY_LENGTH = 2
const DEBOUNCE_MS = 300
/** Catalog search debounce. Shorter than DEBOUNCE_MS: the catalog index is local
 *  and fast, so it can keep up far closer to the typing rate than HB/file search. */
const CATALOG_DEBOUNCE_MS = 105
const MAX_RESULTS_PER_SOURCE = 50

/** Poll interval while the service is still building the catalog TOC index
 *  (first start after a seforim-DB change) — search stays in its loading state. */
const INDEX_NOT_READY_RETRY_MS = 1200
/** Re-issue delay when a request was cancelled server-side by a newer one. */
const SUPERSEDED_RETRY_MS = 150

// Prefixes that shift which source appears first in the dropdown.
// Matched against the beginning of the normalized query (after stripping spaces).
// The backslash shorthands (\\ for files, \ for HebrewBooks) also match at the
// end of the query, and are checked before the Hebrew word prefixes so the
// longer one (\\) is matched first.
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

function stripShorthandFromQuery(trimmed: string): string {
  // A shorthand may appear at both ends; strip both so no stray backslash
  // leaks into the search terms. Colon handling matches stripPrefixFromQuery.
  return trimmed.replace(/^\\+|\\+$/g, '').replace(/^\s*:\s*/, '').trim()
}

function parseQueryPrefix(rawQuery: string): ParsedQuery {
  const trimmed = rawQuery.trim()

  // Check the backslash shorthands first, as prefix or suffix — \\ (files)
  // before \ (HebrewBooks) so the longer match wins when the user types two
  // backslashes at either end.
  if (trimmed.startsWith(FILES_SHORTHAND) || trimmed.endsWith(FILES_SHORTHAND)) {
    return { priority: 'files', effectiveQuery: stripShorthandFromQuery(trimmed) }
  }
  if (trimmed.startsWith(HEBREWBOOKS_SHORTHAND) || trimmed.endsWith(HEBREWBOOKS_SHORTHAND)) {
    return { priority: 'hebrewbooks', effectiveQuery: stripShorthandFromQuery(trimmed) }
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
  // "תוספים" also puts files first, but it is a search TERM rather than a prefix —
  // the file search rewrites it to the index's addin prefix, so it must survive here.
  if (queryTargetsAddins(trimmed)) {
    return { priority: 'files', effectiveQuery: trimmed }
  }
  return { priority: 'catalog', effectiveQuery: trimmed }
}

// ─── Composable ───────────────────────────────────────────────────────────────

export function useGlobalSearch(searchQuery: ReturnType<typeof ref<string>>) {
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
  // Separate counter for the catalog search — the HB/files watcher increments
  // asyncGeneration on its own debounced query, so sharing one counter would
  // make each watcher cancel the other.
  let catalogGeneration = 0
  // The effective query whose catalog results are currently applied (null when
  // the results are cleared). refDebounced writes debouncedCatalogQuery with a
  // plain assignment, so re-typing the SAME text within the debounce window is
  // a no-op write that fires no watcher. Without this, clearing on a
  // sub-minimum query and then restoring the previous text would leave the
  // catalog section permanently empty. Compared against, not watched.
  let appliedCatalogQuery: string | null = null

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)
  const debouncedCatalogQuery = refDebounced(searchQuery, CATALOG_DEBOUNCE_MS)

  /**
   * Split one ranked `catalogTocSearch` response into the dropdown's two refs.
   * Level 0 docs are book-title hits, level 1+ are TOC-path hits; the index's
   * own order is preserved within each, so book hits still lead.
   * Hits whose book is not in the loaded catalog (e.g. a stale index row) drop.
   */
  function applyCatalogHits(hits: CatalogTocServiceHit[]): void {
    const bookById = new Map(booksDataStore.allBooks.map((b) => [b.id, b]))
    const books: CatalogSearchResult[] = []
    const tocItems: TocFsItem[] = []

    for (const hit of hits) {
      const book = bookById.get(hit.bookId)
      if (!book) continue
      if (hit.level === 0) {
        if (books.length < MAX_RESULTS_PER_SOURCE) books.push({ source: 'catalog', book })
      } else if (tocItems.length < MAX_RESULTS_PER_SOURCE) {
        // fullTocPath is "<book title> / <toc path>" — the UI prepends the book
        // title itself, so show only the TOC part. Navigation is line-based.
        const titlePrefix = `${book.title} / `
        const tocPath = hit.fullTocPath.startsWith(titlePrefix)
          ? hit.fullTocPath.slice(titlePrefix.length)
          : hit.fullTocPath
        tocItems.push({
          uid: `toc-${hit.bookId}-${hit.treeOrder}`,
          kind: 'toc',
          book,
          tocEntryId: 0,
          tocLineIndex: hit.lineIndex >= 0 ? hit.lineIndex : null,
          tocTitle: tocPath.split(' / ').pop() ?? tocPath,
          tocPath,
        })
      }
      if (books.length >= MAX_RESULTS_PER_SOURCE && tocItems.length >= MAX_RESULTS_PER_SOURCE) break
    }

    setCatalog(books)
    setCatalogToc(tocItems)
  }

  // ── Catalog search — instant on every keystroke ───────────────────────────

  // ── Catalog search — debounced, via the Lucene catalog TOC index ──────────
  //
  // The SAME engine the catalog page uses (useBookCatalogSearch): one
  // `catalogTocSearch` call answers the whole query. Book-title docs (level 0)
  // and TOC-path docs (level 1+) come back together, ranked by the index, and
  // are split into the two result refs the dropdown renders.
  //
  // This replaced an in-memory inverted-index match. That index knew nothing of
  // the abbreviation map the backend applies (ט"ז → its full title), so the
  // address bar and the catalog page answered the same query differently. The
  // backend expands abbreviations BEFORE stripping punctuation, which a
  // frontend normalizer cannot reproduce without duplicating the map.

  watch(
    searchQuery,
    (rawQuery) => {
      // Priority prefixes drive section ordering and must stay instant — only
      // the catalog results themselves are debounced.
      const { priority, effectiveQuery } = parseQueryPrefix(rawQuery ?? '')
      sourcePriority.value = priority

      // Instant clear so an emptied/too-short input doesn't keep showing stale
      // results for the debounce interval.
      if (effectiveQuery.length < MIN_QUERY_LENGTH) {
        catalogGeneration++
        appliedCatalogQuery = null
        setCatalog([])
        setCatalogToc([])
        isLoadingCatalogToc.value = false
        return
      }

      // Back above the minimum. If the debounced ref already holds this exact
      // query, its next write is a no-op and the search watcher will not fire —
      // so run it here instead. (Typing "ab", then one char, then "ab" again
      // within the debounce window.)
      if (
        appliedCatalogQuery === null &&
        parseQueryPrefix(debouncedCatalogQuery.value ?? '').effectiveQuery === effectiveQuery
      ) {
        void runCatalogSearch(effectiveQuery)
      }
    },
    { immediate: true },
  )

  /**
   * Run one catalog search and apply its results, unless a newer search has
   * started (generation bump) while this one was in flight.
   */
  async function runCatalogSearch(effectiveQuery: string): Promise<void> {
    const generation = ++catalogGeneration

    isLoadingCatalogToc.value = true
    try {
      // Retry while the index is still building — a newer search supersedes the loop.
      for (;;) {
        const res = await catalogTocSearch(effectiveQuery)
        if (generation !== catalogGeneration) return
        // A hard failure (index missing or unreadable) is terminal — retrying it
        // would poll forever and hold the spinner on for the life of the query.
        if (res.error) {
          setCatalog([])
          setCatalogToc([])
          appliedCatalogQuery = effectiveQuery
          break
        }
        if (res.ready && !res.superseded) {
          applyCatalogHits(res.results ?? [])
          appliedCatalogQuery = effectiveQuery
          break
        }
        // superseded (requests raced on the pipe) → re-issue promptly; not-ready
        // (index still building) → poll slowly.
        await new Promise((resolve) =>
          setTimeout(resolve, res.superseded ? SUPERSEDED_RETRY_MS : INDEX_NOT_READY_RETRY_MS),
        )
        if (generation !== catalogGeneration) return
      }
    } catch {
      if (generation === catalogGeneration) {
        setCatalog([])
        setCatalogToc([])
        appliedCatalogQuery = effectiveQuery
      }
    } finally {
      if (generation === catalogGeneration) isLoadingCatalogToc.value = false
    }
  }

  watch(
    debouncedCatalogQuery,
    (rawQuery) => {
      const { effectiveQuery } = parseQueryPrefix(rawQuery ?? '')

      if (effectiveQuery.length < MIN_QUERY_LENGTH) {
        catalogGeneration++
        appliedCatalogQuery = null
        setCatalog([])
        setCatalogToc([])
        isLoadingCatalogToc.value = false
        return
      }

      void runCatalogSearch(effectiveQuery)
    },
    { immediate: true },
  )

  // ── HebrewBooks + file search — debounced ─────────────────────────────────

  watch(
    debouncedQuery,
    async (rawQuery) => {
      const generation = ++asyncGeneration
      const { effectiveQuery } = parseQueryPrefix(rawQuery ?? '')

      if (effectiveQuery.length < MIN_QUERY_LENGTH) {
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

      const filePromise = fileSystemSearch(normalizeAddinQuery(effectiveQuery), 50)
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
            addinName: item.addinName ?? '',
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
    catalogGeneration++
    appliedCatalogQuery = null
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
