import { ref, computed } from 'vue'
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'
import { buildTree } from './booksFsTree'
import type { CategoryNode, CategoryRow, BookRow } from './booksFsTree'

export type FsItem =
  | { uid: string; kind: 'folder'; node: CategoryNode }
  | { uid: string; kind: 'book'; book: BookRow }

const ROOT: CategoryNode = {
  id: -1, parentId: null, title: '', level: -1, orderIndex: 0, children: [], books: [],
}

export function useBooksFs() {
  const loading = ref(true)
  const error = ref<string | null>(null)
  const path = ref<CategoryNode[]>([ROOT])
  const searchQuery = ref('')
  const allBooks = ref<BookRow[]>([])

  const currentNode = computed(() => path.value[path.value.length - 1]!)
  const isSearching = computed(() => searchQuery.value.trim().length > 0)

  const listItems = computed((): FsItem[] => {
    if (isSearching.value) {
      const q = searchQuery.value.trim().toLowerCase()
      return allBooks.value
        .filter(b => b.title.toLowerCase().includes(q))
        .map(b => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b }))
    }
    return [
      ...currentNode.value.children.map(n => ({ uid: `f-${n.id}`, kind: 'folder' as const, node: n })),
      ...currentNode.value.books.map(b => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b })),
    ]
  })

  async function load() {
    loading.value = true
    error.value = null
    try {
      const [categories, books] = await Promise.all([
        query<CategoryRow>(SQL.GET_ALL_CATEGORIES),
        query<BookRow>(SQL.GET_ALL_BOOKS),
      ])
      ROOT.children = buildTree(categories, books)
      allBooks.value = books
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'שגיאה בטעינת הנתונים'
    } finally {
      loading.value = false
    }
  }

  function enter(node: CategoryNode) {
    path.value = [...path.value, node]
    searchQuery.value = ''
  }

  function navigateTo(index: number) {
    path.value = path.value.slice(0, index + 1)
    searchQuery.value = ''
  }

  return { loading, error, path, searchQuery, isSearching, listItems, load, enter, navigateTo }
}
