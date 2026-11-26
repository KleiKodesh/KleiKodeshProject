import { computed, reactive, ref, shallowRef } from 'vue'
import { refDebounced } from '@vueuse/core'
import { getCategoryContents, getCategoryChain } from '@/webview-host/seforimApi'
import { summaryBook } from '@/stores/bookSummaries'
import { useBookCatalogSearch } from './useBookCatalogSearch'
import { useFrequentFoldersStore } from '@/stores/frequentFoldersStore'
import { t } from '@/i18n'
import type { CategoryNode, LocalFileItem } from './bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
export type FsItem =
  | { uid: string; kind: 'folder'; node: CategoryNode }
  | { uid: string; kind: 'book'; book: BookRow }
  | { uid: string; kind: 'file'; file: LocalFileItem }

const ROOT_ID = -1

/** A book row of a browsed folder: its id is known up front, its title/authors load only when a row is rendered. */
function browseBook(id: number, categoryId: number): BookRow {
  const book = summaryBook(id)
  Object.defineProperty(book, 'categoryId', { value: categoryId, enumerable: true })
  return book as unknown as BookRow
}

/**
 * The catalog the page BROWSES, one folder at a time: a folder's subfolders and book ids load
 * when it is opened (Core GetCategoryContents, thin), and book details load per rendered row.
 * Nothing but the folders the user has opened is held.
 */
export function useBookCatalog() {
  const loading = ref(false)
  const error = ref<string | null>(null)

  const rootNode = reactive<CategoryNode>({ id: ROOT_ID, parentId: null, title: '', level: -1, children: [], books: [] }) as CategoryNode
  const ROOT = shallowRef<CategoryNode>(rootNode)

  const folderLoads = new Map<number, Promise<void>>()

  /** Fills a folder's subfolders and book ids once; the same promise answers every caller. */
  function fetchFolder(node: CategoryNode): Promise<void> {
    let load = folderLoads.get(node.id)
    if (!load) {
      load = (async () => {
        const contents = await getCategoryContents(node.id === ROOT_ID ? null : node.id)
        node.children = contents.folders.map((f) =>
          reactive<CategoryNode>({
            id: f.id,
            parentId: node.id === ROOT_ID ? null : node.id,
            title: f.title,
            level: node.level + 1,
            bookCount: f.bookCount,
            children: [],
            books: [],
          }) as CategoryNode,
        )
        node.books = contents.bookIds.map((id) => browseBook(id, node.id))
      })()
      load.catch(() => folderLoads.delete(node.id))
      folderLoads.set(node.id, load)
    }
    return load
  }

  /** Loads a folder; a failure is shown as the page's error and answers false (never an unhandled rejection). */
  async function loadFolder(node: CategoryNode): Promise<boolean> {
    try {
      await fetchFolder(node)
      return true
    } catch (e) {
      const msg = e instanceof Error ? e.message : ''
      if (msg.toLowerCase().includes('failed to fetch')) error.value = t('stores.loadErrorNoServer')
      else if (msg.includes('0x8007000B') || msg.toLowerCase().includes('incorrect format')) error.value = t('stores.loadErrorSqlite')
      else error.value = msg || t('stores.loadError')
      return false
    }
  }

  /** Loads the top level; the page calls it on mount and from the retry button. */
  async function load() {
    loading.value = true
    error.value = null
    await loadFolder(rootNode)
    loading.value = false
  }

  const path = shallowRef<CategoryNode[]>([rootNode])

  const searchQuery = ref('')
  const debouncedQuery = refDebounced(searchQuery, 300)
  const currentNode = computed(() => path.value[path.value.length - 1]!)
  const isSearching = computed(() => debouncedQuery.value.trim().length > 1)

  const treeItems = computed((): FsItem[] => [
    ...currentNode.value.children.map((n) => ({
      uid: `f-${n.id}`,
      kind: 'folder' as const,
      node: n,
    })),
    ...currentNode.value.books.map((b) => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b })),
  ])

  const { results: searchItems, searching: tocSearching } = useBookCatalogSearch(searchQuery)

  const frequentFoldersStore = useFrequentFoldersStore()

  /**
   * Counts a visit to the category at the end of the current path (home's folder
   * cards). Only categories holding books directly count — a category of
   * subcategories alone is a waypoint, not a destination.
   */
  function trackCurrentCategoryVisit() {
    const categories = path.value.slice(1)
    const category = categories[categories.length - 1]
    if (category && category.books.length > 0) frequentFoldersStore.trackCatalogCategoryVisit(category.id, categories.map((c) => c.title))
  }

  async function enter(node: CategoryNode) {
    if (!(await loadFolder(node))) return
    path.value = [...path.value, node]
    searchQuery.value = ''
    trackCurrentCategoryVisit()
  }
  /**
   * Jumps straight to a category by id: its chain comes from Core, each folder along it loads,
   * and the path ends at it. Returns false when the category does not exist or is not browsable.
   */
  async function openCategory(categoryId: number): Promise<boolean> {
    const chain = await getCategoryChain(categoryId)
    if (!chain.length) return false
    if (!(await loadFolder(rootNode))) return false
    const nodes: CategoryNode[] = []
    let parent: CategoryNode = rootNode
    for (const step of chain) {
      const node = parent.children.find((c) => c.id === step.id)
      if (!node) return false
      if (!(await loadFolder(node))) return false
      nodes.push(node)
      parent = node
    }
    path.value = [rootNode, ...nodes]
    searchQuery.value = ''
    trackCurrentCategoryVisit()
    return true
  }
  function navigateTo(index: number) {
    path.value = path.value.slice(0, index + 1)
    searchQuery.value = ''
  }
  /** Navigate to a sibling of the node at `index`, replacing everything from that index onward. */
  async function navigateToSibling(index: number, node: CategoryNode) {
    if (!(await loadFolder(node))) return
    path.value = [...path.value.slice(0, index), node]
    searchQuery.value = ''
    trackCurrentCategoryVisit()
  }

  return {
    ROOT,
    loading,
    error,
    path,
    searchQuery,
    isSearching,
    treeItems,
    searchItems,
    tocSearching,
    load,
    loadFolder,
    enter,
    openCategory,
    navigateTo,
    navigateToSibling,
  }
}
