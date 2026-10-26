import { defineStore } from 'pinia'
import { markRaw, ref, shallowRef } from 'vue'
import { ensureCategorySchema } from '@/webview-host/seforimDb'
import { getAllCategories, getAllBooks } from '@/webview-host/seforimApi'
import {
  buildTree,
  assignFullPaths,
  assignSubtreeBookIds,
  findCategoryMeta,
} from '../features/book-catalog/bookCatalogTree'
import type { CategoryNode } from '../features/book-catalog/bookCatalogTree'
import type { CategoryRow, BookRow } from '@/webview-host/queries.types'
import { t } from '@/i18n'
export const useBooksDataStore = defineStore('booksData', () => {
  const loaded = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)
  // The catalog is thousands of rows read everywhere and never edited in place after
  // load: shallow refs holding raw objects, assigned whole. Deep-proxying every row
  // would tax every read on the hot path for nothing.
  const allBooks = shallowRef<BookRow[]>(markRaw([]))
  const allBooksMap = shallowRef(markRaw(new Map<number, BookRow>()))
  const ROOT = shallowRef<CategoryNode>(
    markRaw({ id: -1, parentId: null, title: '', level: -1, children: [], books: [] }),
  )
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
          ;[categories, books] = await Promise.all([getAllCategories(), getAllBooks()])
        } catch (e) {
          // If the combined fetch fails, try each query individually so a broken
          // books table doesn't prevent categories from loading (and vice versa).
          try { categories = await getAllCategories() } catch { /* use empty */ }
          try { books = await getAllBooks() } catch { /* use empty */ }
          // Only surface an error if we got nothing at all
          if (!categories.length && !books.length) throw e
        }

        const children = buildTree(categories, books)
        assignSubtreeBookIds(children)
        const orderedBooks = assignFullPaths(children)

        ROOT.value = markRaw({ ...ROOT.value, children })
        allBooks.value = markRaw(orderedBooks)
        allBooksMap.value = markRaw(new Map(orderedBooks.map((book) => [book.id, book])))
        loaded.value = true
      } catch (e) {
        const msg = e instanceof Error ? e.message : ''
        if (msg.toLowerCase().includes('failed to fetch')) {
          error.value = t('stores.loadErrorNoServer')
        } else if (msg.includes('0x8007000B') || msg.toLowerCase().includes('incorrect format')) {
          error.value =
            t('stores.loadErrorSqlite')
        } else {
          error.value = msg || t('stores.loadError')
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

      // The rows above were mutated in place (raw, not reactive): reassign both so anything
      // derived from the list or the map recomputes with the period/root metadata.
      allBooks.value = markRaw(allBooks.value.slice())
      allBooksMap.value = markRaw(new Map(allBooks.value.map((book) => [book.id, book])))
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
