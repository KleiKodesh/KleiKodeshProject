import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalize'
import { splitQuery, buildTocSearchPaths, matchWords, normalizeTocWords } from '@/utils/tocSearchSplit'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from './booksFsTree'
import type { TocSearchNode } from '@/utils/tocSearchSplit'

export type BookFsItem = { uid: string; kind: 'book'; book: BookRow }
export type TocFsItem = {
  uid: string
  kind: 'toc'
  book: BookRow
  tocEntryId: number
  tocTitle: string
  tocPath: string
}
export type SearchFsItem = BookFsItem | TocFsItem

// LRU-1 cache — keyed by sorted candidate book IDs
let tocCache: { key: string; nodes: TocSearchNode[] } | null = null

function batchSize(total: number): number {
  return Math.max(1, Math.ceil(Math.sqrt(total)))
}

function chunkIds(ids: number[]): number[][] {
  const size = batchSize(ids.length)
  const chunks: number[][] = []
  for (let i = 0; i < ids.length; i += size) chunks.push(ids.slice(i, i + size))
  return chunks
}

function stripRoots(rows: TocRow[], bookTitles: Map<number, string>): TocRow[] {
  const rootIds = new Set(rows.filter(r => r.parentId === null && r.text === bookTitles.get(r.bookId)).map(r => r.id))
  return rows
    .filter(r => !rootIds.has(r.id))
    .map(r => rootIds.has(r.parentId!) ? { ...r, parentId: null } : r)
}

type TocRow = { id: number; parentId: number | null; bookId: number; text: string }

// Yield to the browser every N nodes to avoid blocking the main thread
const MATCH_YIELD_EVERY = 200

async function matchNodes(nodes: TocSearchNode[], tocWords: string[], bookMap: Map<number, BookRow>): Promise<TocFsItem[]> {
  const normalizedTocWords = normalizeTocWords(tocWords)
  const out: TocFsItem[] = []
  for (let i = 0; i < nodes.length; i++) {
    if (i > 0 && i % MATCH_YIELD_EVERY === 0) await Promise.resolve()
    const node = nodes[i]!
    if (matchWords(node.tocSearchPath, normalizedTocWords)) {
      const book = bookMap.get(node.bookId)
      if (book) out.push({
        uid: `toc-${node.bookId}-${node.id}`,
        kind: 'toc',
        book,
        tocEntryId: node.id,
        tocTitle: node.text,
        tocPath: node.tocDisplayPath,
      })
    }
  }
  return out
}

export function useBooksFsSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 300)

  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)

  function filterBooks(words: string[]): BookRow[] {
    return store.allBooks.filter(b => {
      const p = b.searchPath ?? ''
      return words.every(w => p.includes(w))
    })
  }

  // Phase 1: instant in-memory book search — no debounce needed
  watch(searchQuery, (raw) => {
    const q = normalize(raw.trim())
    if (!q || q.length < 2) { results.value = []; return }
    const words = q.split(/\s+/).filter(w => w.length > 0)
    const bookMatches = filterBooks(words)
    if (bookMatches.length > 0) {
      results.value = bookMatches.map(b => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b }))
    }
  }, { immediate: true })

  // Phase 2: TOC fallback — debounced, only fires when book search found nothing
  watch(debouncedQuery, async (raw) => {
    const q = normalize((raw ?? '').trim())
    if (!q || q.length < 2) { results.value = []; return }

    const words = q.split(/\s+/).filter(w => w.length > 0)

    // Skip if book search already has results
    if (filterBooks(words).length > 0) return

    const split = splitQuery(words, (bw) => filterBooks(bw).length > 0)
    if (!split) { results.value = []; return }

    const { bookWords, tocWords } = split
    const candidateBooks = filterBooks(bookWords)
    if (!candidateBooks.length) { results.value = []; return }

    const bookMap = new Map(candidateBooks.map(b => [b.id, b]))
    const ids = candidateBooks.slice().sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0)).map(b => b.id)
    const cacheKey = ids.join(',')

    if (tocCache?.key === cacheKey) {
      searching.value = true
      try {
        const tocResults = await matchNodes(tocCache.nodes, tocWords, bookMap)
        if (tocResults.length > 0) results.value = tocResults
      } finally {
        searching.value = false
      }
    } else {
      searching.value = true
      results.value = [] // clear stale results from a previous different query
      const allNodes: TocSearchNode[] = []
      const bookTitles = new Map(candidateBooks.map(b => [b.id, b.title]))
      try {
        for (const batch of chunkIds(ids)) {
          const rows = await query<TocRow>(SQL.GET_TOC_TITLES_FOR_BOOKS(batch.length), batch)
          const batchNodes = buildTocSearchPaths(stripRoots(rows, bookTitles))
          batchNodes.sort((a, b) => (bookMap.get(a.bookId)?.treeOrder ?? 0) - (bookMap.get(b.bookId)?.treeOrder ?? 0))
          allNodes.push(...batchNodes)
          const batchResults = await matchNodes(batchNodes, tocWords, bookMap)
          if (batchResults.length > 0) results.value = [...(results.value as TocFsItem[]), ...batchResults]
        }
        tocCache = { key: cacheKey, nodes: allNodes }
      } finally {
        searching.value = false
      }
    }
  }, { immediate: true })

  return { results, searching }
}
