import { defineStore } from 'pinia'
import { ref } from 'vue'
import { query, categoryHasOrderIndex, ensureCategorySchema } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'
import {
  buildTree,
  assignFullPaths,
  findCategoryMeta,
} from '../features/book-catalog/bookCatalogTree'
import { buildSearchIndex } from '../features/book-catalog/bookCatalogSearch'
import type { CategoryNode, CategoryRow, BookRow } from '../features/book-catalog/bookCatalogTree'

export const useBooksDataStore = defineStore('booksData', () => {
  const loaded = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const allBooks = ref<BookRow[]>([])
  const allBooksMap = ref(new Map<number, BookRow>())
  const ROOT = ref<CategoryNode>({
    id: -1,
    parentId: null,
    title: '',
    level: -1,
    children: [],
    books: [],
  })
  let loadPromise: Promise<void> | null = null
  let commentaryMetaPromise: Promise<void> | null = null
  let commentaryMetaLoaded = false

  async function ensureLoaded() {
    if (loaded.value) return
    if (loadPromise) return loadPromise

    loading.value = true
    error.value = null
    loadPromise = (async () => {
      try {
        await ensureCategorySchema()
        let categories: CategoryRow[] = []
        let books: BookRow[] = []
        try {
          ;[categories, books] = await Promise.all([
            query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex)),
            query<BookRow>(SQL.GET_ALL_BOOKS),
          ])
        } catch (e) {
          // If the combined fetch fails, try each query individually so a broken
          // books table doesn't prevent categories from loading (and vice versa).
          try { categories = await query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex)) } catch { /* use empty */ }
          try { books = await query<BookRow>(SQL.GET_ALL_BOOKS) } catch { /* use empty */ }
          // Only surface an error if we got nothing at all
          if (!categories.length && !books.length) throw e
        }

        const children = buildTree(categories, books)
        const orderedBooks = assignFullPaths(children)

        ROOT.value = { ...ROOT.value, children }
        allBooks.value = orderedBooks
        // Build the id→book map immediately so consumers like book-view can read
        // book metadata (e.g. hasTeamim) without waiting for ensureCommentaryMetadataLoaded.
        allBooksMap.value = new Map(orderedBooks.map((book) => [book.id, book]))
        loaded.value = true

        // Build the search index asynchronously in the background — yields between
        // chunks so it doesn't block the first render after catalog load
        buildSearchIndex(orderedBooks)
      } catch (e) {
        const msg = e instanceof Error ? e.message : ''
        if (msg.toLowerCase().includes('failed to fetch')) {
          error.value = 'שגיאה בטעינת הנתונים — לא ניתן להתחבר לשרת'
        } else if (msg.includes('0x8007000B') || msg.toLowerCase().includes('incorrect format')) {
          error.value =
            'שגיאה בטעינת הנתונים — קובץ ה־SQLite אינו תואם לגרסת המערכת (32/64 סיביות). יש להתקין מחדש את האפליקציה.'
        } else {
          error.value = msg || 'שגיאה בטעינת הנתונים'
        }
      } finally {
        loading.value = false
        loadPromise = null
      }
    })()

    return loadPromise
  }

  async function ensureCommentaryMetadataLoaded() {
    await ensureLoaded()
    if (commentaryMetaLoaded) return
    if (commentaryMetaPromise) return commentaryMetaPromise

    commentaryMetaPromise = new Promise<void>((resolve) => setTimeout(resolve, 0)).then(() => {
      const map = new Map<number, CategoryNode>()
      const flattenNodes = (nodes: CategoryNode[]) => {
        for (const node of nodes) {
          map.set(node.id, node)
          flattenNodes(node.children)
        }
      }
      flattenNodes(ROOT.value.children)

      const metaCache = new Map<number, ReturnType<typeof findCategoryMeta>>()
      for (const book of allBooks.value) {
        let meta = metaCache.get(book.categoryId)
        if (!meta) {
          meta = findCategoryMeta(book.categoryId, map)
          metaCache.set(book.categoryId, meta)
        }
        book.period = meta.period ?? 'אחר'
        book.rootCategory = meta.root ?? undefined
      }

      allBooksMap.value = new Map(allBooks.value.map((book) => [book.id, book]))
      commentaryMetaLoaded = true
      commentaryMetaPromise = null
    })

    return commentaryMetaPromise
  }

  return {
    loaded,
    loading,
    error,
    allBooks,
    allBooksMap,
    ensureLoaded,
    ensureCommentaryMetadataLoaded,
    ROOT,
  }
})
