/**
 * File-system search composable.
 *
 * DEV (service-backed): a single call to the service's Lucene catalog TOC index
 * (`catalogTocSearch`) replaces the whole two-phase pipeline below — book-title docs
 * and full-TOC-path docs are searched together and come back ranked. Parity with the
 * manual pipeline is covered by KitveiHakodeshService.Tests (2026-07-17: 0 misses).
 *
 * HOSTED (C#): still the manual two-phase search below — the hosted app has no
 * service, so it keeps the in-memory book match + TOC heuristics for now.
 *
 * Two-phase search (hosted path):
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
import { isDbHosted } from '@/webview-host/seforimApi'
import { serviceCall } from '@/webview-host/serviceClient'
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

// ─── Lucene service search (dev) ──────────────────────────────────────────────

/** One `catalogTocSearch` hit on the wire (serviceClient camelCases the msgpack keys). */
type CatalogTocServiceHit = {
  bookId: number
  lineIndex: number // -1 = no resolved line
  /** Display path: book title, then " / "-joined TOC segments. */
  fullTocPath: string
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
        // fullTocPath is "<book title> / <toc path>" — the UI prepends the book title
        // itself, so show only the TOC part. Navigation is line-based (tocEntryId is
        // no longer carried by the index).
        const titlePrefix = `${book.title} / `
        const tocPath = hit.fullTocPath.startsWith(titlePrefix)
          ? hit.fullTocPath.slice(titlePrefix.length)
          : hit.fullTocPath
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
          const res = await serviceCall<CatalogTocServiceResult>('catalogTocSearch', { query })
          if (generation !== searchGeneration) return
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
        if (generation === searchGeneration) results.value = []
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
  // Dev talks to the service → Lucene only. Hosted C# keeps the manual pipeline below.
  if (!isDbHosted()) return useLuceneCatalogSearch(searchQuery)

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
