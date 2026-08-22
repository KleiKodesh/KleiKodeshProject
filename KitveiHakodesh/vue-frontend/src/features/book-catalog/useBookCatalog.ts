import { computed, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { refDebounced } from '@vueuse/core'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useBookCatalogSearch } from './useBookCatalogSearch'
import { withoutHiddenCategories } from './bookCatalogTree'
import type { CategoryNode } from './bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
export type FsItem =
  | { uid: string; kind: 'folder'; node: CategoryNode }
  | { uid: string; kind: 'book'; book: BookRow }

export function useBookCatalog() {
  const store = useBooksDataStore()
  const { loading, error, ROOT: FULL_ROOT } = storeToRefs(store)

  // The root the catalog BROWSES: the store's, minus the categories that are not
  // seforim shelves. Only browsing is filtered — catalog search and the rest of
  // the app read the store's own root, so nothing becomes unreachable.
  const ROOT = computed<CategoryNode>(() => ({
    ...FULL_ROOT.value,
    children: withoutHiddenCategories(FULL_ROOT.value.children),
  }))

  const path = ref<CategoryNode[]>([ROOT.value])

  watch(
    ROOT,
    (root) => {
      path.value = path.value.length === 1 ? [root] : [root, ...path.value.slice(1)]
    },
    { immediate: true },
  )

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

  function enter(node: CategoryNode) {
    path.value = [...path.value, node]
    searchQuery.value = ''
  }
  function navigateTo(index: number) {
    path.value = path.value.slice(0, index + 1)
    searchQuery.value = ''
  }
  /** Navigate to a sibling of the node at `index`, replacing everything from that index onward. */
  function navigateToSibling(index: number, node: CategoryNode) {
    path.value = [...path.value.slice(0, index), node]
    searchQuery.value = ''
  }

  return {
    loading,
    error,
    path,
    searchQuery,
    isSearching,
    treeItems,
    searchItems,
    tocSearching,
    load: store.ensureLoaded,
    enter,
    navigateTo,
    navigateToSibling,
  }
}
