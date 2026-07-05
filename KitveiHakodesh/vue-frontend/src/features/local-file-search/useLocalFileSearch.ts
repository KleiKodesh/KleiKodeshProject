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

import { ref, watch, onUnmounted } from 'vue'
import { refDebounced } from '@vueuse/core'
import { fileSystemSearch, fileSystemSearchWarmup } from '@/webview-host/bridge'
import { usePaneNavigation } from '@/composables/usePaneNavigation'

export interface LocalFileSearchResult {
  fileName: string
  path: string
  fullPath: string
}

const DEBOUNCE_MS = 200
const MAX_RESULTS = 5000
const LOADING_ANIMATION_DELAY_MS = 200

export function useLocalFileSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const results = ref<LocalFileSearchResult[]>([])
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
  // The component is a singleton (not keyed), so onMounted only fires once. Watching
  // the active route fires on first mount and on every subsequent navigation back here.
  const paneNavigation = usePaneNavigation()
  watch(
    () => paneNavigation.activeTab.route,
    (route) => { if (route === '/file-search') fileSystemSearchWarmup() },
    { immediate: true },
  )

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)
  let generation = 0

  async function runSearch(rawQuery: string) {
    const thisGeneration = ++generation
    errorMessage.value = null

    const trimmed = (rawQuery ?? '').trim()
    if (!trimmed) {
      results.value = []
      totalCount.value = 0
      searching.value = false
      return
    }

    searching.value = true
    startLoadingAnimationTimer()

    try {
      const response = await fileSystemSearch(trimmed, MAX_RESULTS)
      if (thisGeneration !== generation) return

      if (response.error) {
        errorMessage.value = response.error
        results.value = []
        totalCount.value = 0
        return
      }

      totalCount.value = response.total ?? 0
      results.value = (response.results ?? []).map((item) => ({
        fileName: item.fileName,
        path: item.path,
        fullPath: item.path ? `${item.path}\\${item.fileName}` : item.fileName,
      }))
    } catch (error) {
      if (thisGeneration !== generation) return
      errorMessage.value = error instanceof Error ? error.message : 'שגיאה בחיפוש'
      results.value = []
      totalCount.value = 0
    } finally {
      if (thisGeneration === generation) {
        searching.value = false
        cancelLoadingAnimationTimer()
      }
    }
  }

  watch(debouncedQuery, (rawQuery) => runSearch(rawQuery ?? ''), { immediate: true })

  return {
    results,
    searching,
    showLoadingAnimation,
    totalCount,
    errorMessage,
  }
}
