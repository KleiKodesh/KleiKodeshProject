/**
 * Local file search composable.
 *
 * Flow:
 * The user types a query → debounce fires → fileSystemSearch() is called.
 * C# (or the Vite dev middleware) starts the DocumentLocator service on demand,
 * waits until the index is ready, then executes the search. The whole thing is
 * one blocking call from Vue's perspective — Vue's loading animation covers the
 * wait. If the service fails to start or the index errors, { error } is returned
 * and shown as an error banner. No page-load handshake, no push events, no
 * separate indexing state.
 */

import { ref, watch, computed, onUnmounted } from 'vue'
import { refDebounced } from '@vueuse/core'
import { fileSystemSearch, fileSystemSearchWarmup } from '@/webview-host/bridge'
import { usePaneNavigation } from '@/composables/usePaneNavigation'

export interface LocalFileSearchResult {
  fileName: string
  path: string
  fullPath: string
  modifiedDate: number
  /** Non-empty only for Otzaria addin entry-point files. Value is "תוסף אוצריא: {name}". */
  addinName: string
}

export type LocalFileSearchSortOrder = 'relevance' | 'fileName' | 'fullPath' | 'date' | 'fileType'

const DEBOUNCE_MS = 200
const MAX_RESULTS = 5000
const LOADING_ANIMATION_DELAY_MS = 200

export function useLocalFileSearch(
  searchQuery: ReturnType<typeof ref<string>>,
  sortOrder: ReturnType<typeof ref<LocalFileSearchSortOrder>>,
) {
  const rawResults = ref<LocalFileSearchResult[]>([])
  const searching = ref(false)
  const showLoadingAnimation = ref(false)
  const totalCount = ref(0)
  const errorMessage = ref<string | null>(null)

  let loadingAnimationTimer: ReturnType<typeof setTimeout> | null = null

  function startLoadingAnimationTimer() {
    cancelLoadingAnimationTimer()
    loadingAnimationTimer = setTimeout(() => {
      showLoadingAnimation.value = true
    }, LOADING_ANIMATION_DELAY_MS)
  }

  function cancelLoadingAnimationTimer() {
    if (loadingAnimationTimer !== null) {
      clearTimeout(loadingAnimationTimer)
      loadingAnimationTimer = null
    }
    showLoadingAnimation.value = false
  }

  onUnmounted(() => cancelLoadingAnimationTimer())

  // Warm up the service every time the user navigates to this page — fire-and-forget.
  const paneNavigation = usePaneNavigation()
  watch(
    () => paneNavigation.activeTab.route,
    (route) => {
      if (route === '/file-search') fileSystemSearchWarmup()
    },
    { immediate: true },
  )

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)
  let generation = 0

  async function runSearch(rawQuery: string) {
    const thisGeneration = ++generation
    errorMessage.value = null

    const trimmed = (rawQuery ?? '').trim()
    if (!trimmed) {
      rawResults.value = []
      totalCount.value = 0
      searching.value = false
      return
    }

    // Normalize תוספים (with or without trailing colon) to the baked index prefix
    // "תוסף אוצריא:" so users get addin results whether they type the shorthand or the
    // full prefix. The normalized query hits the same *word* wildcards as any other term.
    const normalizedQuery = trimmed.replace(/תוספים:?\s*/g, 'תוסף אוצריא: ')

    searching.value = true
    startLoadingAnimationTimer()

    try {
      const response = await fileSystemSearch(normalizedQuery, MAX_RESULTS)
      if (thisGeneration !== generation) return

      if (response.error) {
        errorMessage.value = response.error
        rawResults.value = []
        totalCount.value = 0
        return
      }

      totalCount.value = response.total ?? 0
      rawResults.value = (response.results ?? []).map((item) => ({
        fileName: item.fileName,
        path: item.path,
        fullPath: item.path ? `${item.path}\\${item.fileName}` : item.fileName,
        modifiedDate: item.modifiedDate ?? 0,
        addinName: (item as any).addinName ?? '',
      }))
    } catch (error) {
      if (thisGeneration !== generation) return
      errorMessage.value = error instanceof Error ? error.message : 'שגיאה בחיפוש'
      rawResults.value = []
      totalCount.value = 0
    } finally {
      if (thisGeneration === generation) {
        searching.value = false
        cancelLoadingAnimationTimer()
      }
    }
  }

  watch(debouncedQuery, (rawQuery) => runSearch(rawQuery ?? ''), { immediate: true })

  // Sort the raw results according to the chosen sort order.
  // "relevance" preserves the original index order (Lucene's natural order).
  const results = computed<LocalFileSearchResult[]>(() => {
    const order = sortOrder.value
    if (order === 'relevance') return rawResults.value

    const copy = rawResults.value.slice()
    if (order === 'fileName') {
      copy.sort((firstItem, secondItem) =>
        firstItem.fileName.localeCompare(secondItem.fileName, 'he'),
      )
    } else if (order === 'fullPath') {
      copy.sort((firstItem, secondItem) =>
        firstItem.fullPath.localeCompare(secondItem.fullPath, 'he'),
      )
    } else if (order === 'date') {
      // Most recent first
      copy.sort((firstItem, secondItem) => secondItem.modifiedDate - firstItem.modifiedDate)
    } else if (order === 'fileType') {
      copy.sort((firstItem, secondItem) => {
        const extensionOfFirst = firstItem.fileName.split('.').pop()?.toLowerCase() ?? ''
        const extensionOfSecond = secondItem.fileName.split('.').pop()?.toLowerCase() ?? ''
        const extensionComparison = extensionOfFirst.localeCompare(extensionOfSecond)
        if (extensionComparison !== 0) return extensionComparison
        return firstItem.fileName.localeCompare(secondItem.fileName, 'he')
      })
    }
    return copy
  })

  return {
    results,
    searching,
    showLoadingAnimation,
    totalCount,
    errorMessage,
  }
}
