import { normalize } from '@/utils/normalizeText'
import { normalizeBookPath } from '../book-catalog/bookCatalogSearchNormalizer'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useTabStore } from '@/stores/tabStore'
import { filterBooksByWords } from '../book-catalog/bookCatalogSearch'
import { runTocHeuristics } from '../book-catalog/bookCatalogSearchTocHeuristics'

/**
 * Navigate to the daf yomi entry in book-view.
 * Prepends "בבלי" to the query to avoid matching Mishna tractates.
 * Uses the same catalog search pipeline as the book catalog search UI —
 * filterBooksByWords for the book, then runTocHeuristics for the daf entry.
 */
export async function navigateToDafYomi(dafYomi: string): Promise<void> {
  const store = useBooksDataStore()
  const tabStore = useTabStore()

  await store.ensureLoaded()

  // Prepend בבלי so we match Talmud Bavli, not Mishna tractates
  const fullQuery = `בבלי ${dafYomi}`
  const words = normalizeBookPath(normalize(fullQuery.trim()))
    .split(/\s+/)
    .filter((w) => w.length > 0)
  if (!words.length) return

  const { items, splitFound } = await runTocHeuristics(
    words,
    (bookWords) => filterBooksByWords(store.allBooks, bookWords),
    () => false,
  )

  if (items.length > 0) {
    const first = items[0]!
    tabStore.updateActiveTab({
      route: '/book-view',
      title: first.book.title,
      bookId: first.book.id,
      openTocEntryId: first.tocEntryId,
      openTocLineIndex: first.tocLineIndex ?? undefined,
    })
    return
  }

  // splitFound but no TOC match — open the book at the start
  if (splitFound) {
    const bookWords = words.slice(0, words.length - 1)
    const candidates = filterBooksByWords(store.allBooks, bookWords)
    if (candidates.length) {
      const book = candidates[0]!
      tabStore.updateActiveTab({ route: '/book-view', title: book.title, bookId: book.id })
    }
  }
}
