import { computed, ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useBooksFsSearch } from './useBooksFsSearch'
import type { CategoryNode, BookRow } from './booksFsTree'

export type { BookFsItem, TocFsItem, SearchFsItem } from './useBooksFsSearch'

export type FsItem =
  | { uid: string; kind: 'folder'; node: CategoryNode }
  | { uid: string; kind: 'book'; book: BookRow }

export function useBooksFs() {
  const store = useBooksDataStore()

  const path = ref<CategoryNode[]>([store.ROOT])

  watch(() => store.ROOT, (root) => {
    if (path.value.length === 1) path.value = [root]
    else path.value = [root, ...path.value.slice(1)]
  }, { immediate: true })

  const searchQuery = ref('')
  const debouncedQuery = refDebounced(searchQuery, 300)

  const currentNode = computed(() => path.value[path.value.length - 1]!)
  const isSearching = computed(() => debouncedQuery.value.trim().length > 1)

  const treeItems = computed((): FsItem[] => [
    ...currentNode.value.children.map(n => ({ uid: `f-${n.id}`, kind: 'folder' as const, node: n })),
    ...currentNode.value.books.map(b => ({ uid: `b-${b.id}`, kind: 'book' as const, book: b })),
  ])

  const { results: searchItems, searching: tocSearching } = useBooksFsSearch(searchQuery)

  function enter(node: CategoryNode) {
    path.value = [...path.value, node]
    searchQuery.value = ''
  }

  function navigateTo(index: number) {
    path.value = path.value.slice(0, index + 1)
    searchQuery.value = ''
  }

  return {
    loading: computed(() => store.loading),
    error: computed(() => store.error),
    path,
    searchQuery,
    isSearching,
    treeItems,
    searchItems,
    tocSearching,
    load: store.ensureLoaded,
    enter,
    navigateTo,
  }
}
