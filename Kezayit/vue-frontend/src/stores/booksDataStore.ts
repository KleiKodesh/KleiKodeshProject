import { defineStore } from 'pinia'
import { ref } from 'vue'
import { query, categoryHasOrderIndex, ensureCategorySchema } from '@/host/seforimDb'
import { SQL } from '@/host/queries.sql'
import {
  buildTree,
  assignFullPaths,
  findCategoryMeta,
} from '@/components/books-fs/booksCategoryTree'
import type { CategoryNode, CategoryRow, BookRow } from '@/components/books-fs/booksCategoryTree'

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
  const staticCommentaryBooks = ref<BookRow[]>([])
  let staticCommentaryPromise: Promise<void> | null = null

  async function ensureLoaded() {
    if (loaded.value) return
    if (loadPromise) return loadPromise

    loading.value = true
    error.value = null
    loadPromise = (async () => {
      try {
        await ensureCategorySchema()
        const [categories, books] = await Promise.all([
          query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex)),
          query<BookRow>(SQL.GET_ALL_BOOKS),
        ])
        const children = buildTree(categories, books)
        const orderedBooks = assignFullPaths(children)

        ROOT.value = { ...ROOT.value, children }
        allBooks.value = orderedBooks
        loaded.value = true
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

    commentaryMetaPromise = Promise.resolve().then(() => {
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

  async function ensureStaticCommentaryBooksLoaded() {
    await ensureLoaded()
    if (staticCommentaryBooks.value.length > 0) return
    if (staticCommentaryPromise) return staticCommentaryPromise

    staticCommentaryPromise = Promise.resolve().then(async () => {
      const books = await query<{ id: number; title: string }>(SQL.GET_STATIC_COMMENTARY_BOOKS)
      // Convert to BookRow format by finding the full book data
      staticCommentaryBooks.value = books
        .map((b) => allBooksMap.value.get(b.id))
        .filter((b): b is BookRow => b !== undefined)
      staticCommentaryPromise = null
    })

    return staticCommentaryPromise
  }

  return {
    loaded,
    loading,
    error,
    allBooks,
    allBooksMap,
    staticCommentaryBooks,
    ensureLoaded,
    ensureCommentaryMetadataLoaded,
    ensureStaticCommentaryBooksLoaded,
    ROOT,
  }
})
