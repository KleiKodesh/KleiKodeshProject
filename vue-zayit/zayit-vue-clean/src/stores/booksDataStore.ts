import { defineStore } from 'pinia'
import { ref } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'
import { buildTree, assignFullPaths, findCategoryHierarchy, findCategoryPeriod } from '@/components/books-fs/booksFsTree'
import type { CategoryNode, CategoryRow, BookRow } from '@/components/books-fs/booksFsTree'

export const useBooksDataStore = defineStore('booksData', () => {
  const loaded = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const allBooks = ref<BookRow[]>([])
  const ROOT = ref<CategoryNode>({
    id: -1, parentId: null, title: '', level: -1, orderIndex: 0, children: [], books: [],
  })

  async function ensureLoaded() {
    if (loaded.value || loading.value) return
    loading.value = true
    error.value = null
    try {
      const [categories, books] = await Promise.all([
        query<CategoryRow>(SQL.GET_ALL_CATEGORIES),
        query<BookRow>(SQL.GET_ALL_BOOKS),
      ])
      const children = buildTree(categories, books)
      assignFullPaths(children)

      // Build flat category map for hierarchy lookups
      const categoryMap = new Map<number, CategoryNode>()
      const flattenNodes = (nodes: CategoryNode[]) => {
        for (const node of nodes) { categoryMap.set(node.id, node); flattenNodes(node.children) }
      }
      flattenNodes(children)

      // Assign period and category hierarchy to all books
      for (const book of books) {
        book.period = findCategoryPeriod(book.categoryId, categoryMap) ?? 'אחר'
        const h = findCategoryHierarchy(book.categoryId, categoryMap)
        book.rootCategory = h.root ?? undefined
        book.secondaryCategory = h.secondary ?? undefined
        book.rootCategoryOrder = h.rootOrder ?? undefined
        book.secondaryCategoryOrder = h.secondaryOrder ?? undefined
      }

      ROOT.value = { ...ROOT.value, children }
      allBooks.value = books.slice().sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
      loaded.value = true
    } catch (e) {
      const msg = e instanceof Error ? e.message : ''
      error.value = msg.toLowerCase().includes('failed to fetch') ? 'שגיאה בטעינת הנתונים — לא ניתן להתחבר לשרת' : (msg || 'שגיאה בטעינת הנתונים')
    } finally {
      loading.value = false
    }
  }

  return { loaded, loading, error, allBooks, ensureLoaded, ROOT }
})
