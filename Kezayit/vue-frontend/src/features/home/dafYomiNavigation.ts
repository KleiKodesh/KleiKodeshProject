import { normalize } from '@/utils/normalizeText'
import { splitQuery, SearchableTree, stripTocTitleRoots } from '../book-view/tocSearchUtils'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useTabStore } from '@/stores/tabStore'
import { filterBooksByWords } from '../book-catalog/bookCatalogSearch'
import type { BookRow } from '../book-catalog/bookCatalogTree'

type TocRow = {
  id: number
  parentId: number | null
  bookId: number
  text: string
  lineIndex: number | null
  hasChildren: number | boolean
}

/**
 * Navigate to the daf yomi entry in book-view.
 * Prepends "בבלי" to the query to avoid matching Mishna tractates.
 * Among TOC matches, prefers the shortest path (fewest total words = most precise match).
 */
export async function navigateToDafYomi(dafYomi: string): Promise<void> {
  const store = useBooksDataStore()
  const tabStore = useTabStore()

  await store.ensureLoaded()

  // Prepend בבלי so we match Talmud Bavli, not Mishna
  const fullQuery = `בבלי ${dafYomi}`
  const words = normalize(fullQuery.trim()).split(/\s+/).filter((w) => w.length > 0)
  if (!words.length) return

  const split = splitQuery(words, (bw) => filterBooksByWords(store.allBooks, bw).length > 0)
  if (!split) return

  const { bookWords, tocWords } = split
  if (!tocWords.length) return

  const candidateBooks = filterBooksByWords(store.allBooks, bookWords)
  if (!candidateBooks.length) return

  const bookMap = new Map(candidateBooks.map((b) => [b.id, b]))
  const ids = candidateBooks.map((b) => b.id)
  const bookTitles = new Map(candidateBooks.map((b) => [b.id, b.title]))
  const tocQuery = tocWords.join(' ')

  // Fetch TOC rows for all candidate books in one shot (small set for a single tractate)
  const rows = await query<TocRow>(SQL.GET_TOC_TITLES_FOR_BOOKS(ids.length), ids)

  // Strip root entries that duplicate the book title
  const byBook = new Map<number, TocRow[]>()
  for (const r of rows) {
    const group = byBook.get(r.bookId) ?? []
    group.push(r)
    byBook.set(r.bookId, group)
  }
  const stripped: TocRow[] = []
  for (const [bookId, group] of byBook) {
    stripped.push(...stripTocTitleRoots(group, bookTitles.get(bookId) ?? '', { bookId }))
  }

  const tree = new SearchableTree(stripped)
  const matched = tree.search(stripped, tocQuery)
  if (!matched.length) {
    // Fall back: open the book at the start
    const book = candidateBooks[0]!
    tabStore.updateActiveTab({ route: '/book-view', title: book.title, bookId: book.id })
    return
  }

  // Among matches, prefer the one with the shortest display path (fewest extra words = best fit)
  const best = matched.reduce((a, b) => {
    const pathA = tree.displayPaths.get(a.id) ?? ''
    const pathB = tree.displayPaths.get(b.id) ?? ''
    return pathA.length <= pathB.length ? a : b
  })

  const bestRow = best as TocRow
  const book = bookMap.get(bestRow.bookId)
  if (!book) return

  tabStore.updateActiveTab({
    route: '/book-view',
    title: book.title,
    bookId: book.id,
    openTocEntryId: best.id,
    openTocLineIndex: bestRow.lineIndex ?? undefined,
  })
}
