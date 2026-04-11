import { ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { normalize } from '@/utils/normalizeText'
import { splitQuery, SearchableTree } from '@/utils/tocSearchUtils'
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { BookRow } from './booksCategoryTree'

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

let tocCache: { key: string; tree: SearchableTree; rows: TocRow[] } | null = null

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
    const exactWords = words.slice(0, -1)
    const prefixWord = words[words.length - 1]!
    return store.allBooks
      .filter((b) => {
        const pathWords = (b.searchPath ?? '').split(/\s+/)
        const exactOk = exactWords.every((qw) => pathWords.some((pw) => pw === qw))
        const prefixOk = pathWords.some((pw) => pw.includes(prefixWord))
        return exactOk && prefixOk
      })
      .sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
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
      if (!tocWords.length) return
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
      const tocQuery = tocWords.join(' ')

      if (tocCache?.key === cacheKey) {
        searching.value = true
        try {
          const matched = tocCache.tree.search(tocCache.rows, tocQuery)
          const items = matched.flatMap((node) => {
            const book = bookMap.get((node as TocRow).bookId)
            if (!book) return []
            return [
              {
                uid: `toc-${(node as TocRow).bookId}-${node.id}`,
                kind: 'toc' as const,
                book,
                tocEntryId: node.id,
                tocLineIndex: (node as TocRow).lineIndex,
                tocTitle: node.text,
                tocPath: tocCache!.tree.displayPaths.get(node.id) ?? node.text,
              },
            ]
          })
          if (items.length) results.value = items
        } finally {
          searching.value = false
        }
        return
      }

      searching.value = true
      results.value = []
      const allRows: TocRow[] = []
      const bookTitles = new Map(candidateBooks.map((b) => [b.id, b.title]))
      try {
        for (const batch of chunkIds(ids)) {
          const rows = await query<TocRow>(SQL.GET_TOC_TITLES_FOR_BOOKS(batch.length), batch)
          const stripped = stripRoots(rows, bookTitles)
          stripped.sort(
            (a, b) =>
              (bookMap.get(a.bookId)?.treeOrder ?? 0) - (bookMap.get(b.bookId)?.treeOrder ?? 0),
          )
          allRows.push(...stripped)
          // yield to keep UI responsive
          await Promise.resolve()
        }
        const tree = new SearchableTree(allRows)
        tocCache = { key: cacheKey, tree, rows: allRows }
        const matched = tree.search(allRows, tocQuery)
        const items = matched.flatMap((node) => {
          const book = bookMap.get((node as TocRow).bookId)
          if (!book) return []
          return [
            {
              uid: `toc-${(node as TocRow).bookId}-${node.id}`,
              kind: 'toc' as const,
              book,
              tocEntryId: node.id,
              tocLineIndex: (node as TocRow).lineIndex,
              tocTitle: node.text,
              tocPath: tree.displayPaths.get(node.id) ?? node.text,
            },
          ]
        })
        if (items.length) results.value = items
      } finally {
        searching.value = false
      }
    },
    { immediate: true },
  )

  return { results, searching }
}
