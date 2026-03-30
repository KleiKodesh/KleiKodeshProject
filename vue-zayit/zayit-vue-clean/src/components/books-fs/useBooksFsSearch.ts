import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalize'
import {
  splitQuery,
  buildTocSearchPaths,
  matchWords,
  normalizeTocWords,
} from '@/utils/tocSearchSplit'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from './booksFsTree'
import type { TocSearchNode } from '@/utils/tocSearchSplit'

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

type TocRow = {
  id: number
  parentId: number | null
  bookId: number
  text: string
  lineIndex: number | null
}

let tocCache: { key: string; nodes: TocSearchNode[] } | null = null

function chunkIds(ids: number[]): number[][] {
  const size = Math.max(1, Math.ceil(Math.sqrt(ids.length)))
  const out: number[][] = []
  for (let i = 0; i < ids.length; i += size) out.push(ids.slice(i, i + size))
  return out
}

function stripRoots(rows: TocRow[], bookTitles: Map<number, string>): TocRow[] {
  const rootIds = new Set(
    rows.filter((r) => r.parentId === null && r.text === bookTitles.get(r.bookId)).map((r) => r.id),
  )
  return rows
    .filter((r) => !rootIds.has(r.id))
    .map((r) => (rootIds.has(r.parentId!) ? { ...r, parentId: null } : r))
}

async function matchNodes(
  nodes: TocSearchNode[],
  tocWords: string[],
  bookMap: Map<number, BookRow>,
): Promise<TocFsItem[]> {
  const normalized = normalizeTocWords(tocWords)
  const out: TocFsItem[] = []
  for (let i = 0; i < nodes.length; i++) {
    if (i > 0 && i % 200 === 0) await Promise.resolve()
    const node = nodes[i]!
    const book = matchWords(node.tocSearchPath, normalized) ? bookMap.get(node.bookId) : undefined
    if (book)
      out.push({
        uid: `toc-${node.bookId}-${node.id}`,
        kind: 'toc',
        book,
        tocEntryId: node.id,
        tocLineIndex: node.lineIndex,
        tocTitle: node.text,
        tocPath: node.tocDisplayPath,
      })
  }
  return out
}

function toWords(raw: string) {
  return normalize(raw.trim())
    .split(/\s+/)
    .filter((w) => w.length > 0)
}

export function useBooksFsSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const store = useBooksDataStore()
  const debouncedQuery = refDebounced(searchQuery, 300)
  const results = ref<SearchFsItem[]>([])
  const searching = ref(false)

  function filterBooks(words: string[]) {
    return store.allBooks.filter((b) => words.every((w) => (b.searchPath ?? '').includes(w)))
  }

  // Phase 1: instant book match
  watch(
    searchQuery,
    (raw) => {
      const words = toWords(raw ?? '')
      if (words.length === 0) {
        results.value = []
        return
      }
      const matches = filterBooks(words)
      if (matches.length)
        results.value = matches.map((b) => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b }))
    },
    { immediate: true },
  )

  // Phase 2: TOC fallback when book search yields nothing
  watch(
    debouncedQuery,
    async (raw) => {
      const words = toWords(raw ?? '')
      if (words.length === 0) {
        results.value = []
        return
      }
      if (filterBooks(words).length > 0) return

      const split = splitQuery(words, (bw) => filterBooks(bw).length > 0)
      if (!split) {
        results.value = []
        return
      }

      const { bookWords, tocWords } = split
      const candidateBooks = filterBooks(bookWords)
      if (!candidateBooks.length) {
        results.value = []
        return
      }

      const bookMap = new Map(candidateBooks.map((b) => [b.id, b]))
      const ids = candidateBooks
        .slice()
        .sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
        .map((b) => b.id)
      const cacheKey = ids.join(',')

      if (tocCache?.key === cacheKey) {
        searching.value = true
        try {
          const r = await matchNodes(tocCache.nodes, tocWords, bookMap)
          if (r.length) results.value = r
        } finally {
          searching.value = false
        }
        return
      }

      searching.value = true
      results.value = []
      const allNodes: TocSearchNode[] = []
      const bookTitles = new Map(candidateBooks.map((b) => [b.id, b.title]))
      try {
        for (const batch of chunkIds(ids)) {
          const rows = await query<TocRow>(SQL.GET_TOC_TITLES_FOR_BOOKS(batch.length), batch)
          const batchNodes = buildTocSearchPaths(stripRoots(rows, bookTitles))
          batchNodes.sort(
            (a, b) =>
              (bookMap.get(a.bookId)?.treeOrder ?? 0) - (bookMap.get(b.bookId)?.treeOrder ?? 0),
          )
          allNodes.push(...batchNodes)
          const r = await matchNodes(batchNodes, tocWords, bookMap)
          if (r.length) results.value = [...(results.value as TocFsItem[]), ...r]
        }
        tocCache = { key: cacheKey, nodes: allNodes }
      } finally {
        searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}
