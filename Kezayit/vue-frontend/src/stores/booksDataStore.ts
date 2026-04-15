import { defineStore } from 'pinia'
import { ref } from 'vue'
import { query, categoryHasOrderIndex, ensureCategorySchema } from '@/host/db'
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
  const categoryMap = ref(new Map<number, CategoryNode>())
  // Cached map for O(1) book lookups — built once alongside allBooks, never rebuilt
  const allBooksMap = ref(new Map<number, BookRow>())
  const ROOT = ref<CategoryNode>({
    id: -1,
    parentId: null,
    title: '',
    level: -1,
    children: [],
    books: [],
  })

  async function ensureLoaded() {
    if (loaded.value || loading.value) return
    loading.value = true
    error.value = null
    try {
      await ensureCategorySchema()
      const [categories, books] = await Promise.all([
        query<CategoryRow>(SQL.GET_ALL_CATEGORIES(categoryHasOrderIndex)),
        query<BookRow>(SQL.GET_ALL_BOOKS),
      ])
      const children = buildTree(categories, books)
      assignFullPaths(children)

      // Build flat category map for hierarchy lookups
      const map = new Map<number, CategoryNode>()
      const flattenNodes = (nodes: CategoryNode[]) => {
        for (const node of nodes) {
          map.set(node.id, node)
          flattenNodes(node.children)
        }
      }
      flattenNodes(children)
      categoryMap.value = map

      // Assign period and category hierarchy to all books (cached per categoryId)
      const metaCache = new Map<number, ReturnType<typeof findCategoryMeta>>()
      for (const book of books) {
        let meta = metaCache.get(book.categoryId)
        if (!meta) {
          meta = findCategoryMeta(book.categoryId, map)
          metaCache.set(book.categoryId, meta)
        }
        book.period = meta.period ?? 'אחר'
        book.rootCategory = meta.root ?? undefined
      }

      ROOT.value = { ...ROOT.value, children }
      allBooks.value = books.slice().sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
      allBooksMap.value = new Map(allBooks.value.map((b) => [b.id, b]))
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
    }
  }

  return { loaded, loading, error, allBooks, allBooksMap, categoryMap, ensureLoaded, ROOT }
})
