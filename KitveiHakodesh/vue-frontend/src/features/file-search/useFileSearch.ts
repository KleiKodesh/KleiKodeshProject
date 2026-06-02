/**
 * File-system search composable.
 *
 * Flow:
 * 1. On mount, ask C# "is Everything ready?" via fileSystemSearchPageLoad().
 *    C# replies immediately with { isReady: bool }.
 *    If isReady=true  → clear spinner, search works immediately.
 *    If isReady=false → keep spinner; C# is launching Everything in the background
 *                       and will push fileSystemIndexingStatus { isIndexing: false }
 *                       when ready. The watcher on isIndexing then auto-runs any
 *                       pending query.
 * 2. Search only fires when isIndexing=false.
 * 3. If the user typed before ready, the isIndexing watcher retries automatically.
 */

import { ref, watch, onMounted } from 'vue'
import { refDebounced } from '@vueuse/core'
import { fileSystemSearch, fileSystemSearchPageLoad } from '@/webview-host/bridge'
import { isHosted, onWebviewEvent } from '@/webview-host/seforimDb'

export interface FileSearchResult {
  fileName: string
  path: string
  fullPath: string
}

const DEBOUNCE_MS = 300
const MAX_RESULTS = 5000

export function useFileSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const results = ref<FileSearchResult[]>([])
  const searching = ref(false)
  const isIndexing = ref(true) // true = not ready yet; false = ready to search
  const totalCount = ref(0)
  const errorMessage = ref<string | null>(null)

  onMounted(() => {
    if (!isHosted) {
      isIndexing.value = false
      return
    }

    // Register the listener FIRST — before calling pageLoad — so we cannot
    // miss a push that arrives before the pageLoad promise resolves.
    onWebviewEvent((event: any) => {
      if (event.event === 'fileSystemIndexingStatus') {
        isIndexing.value = event.isIndexing
      }
    })

    // Ask C# if Everything is ready right now.
    // C# replies immediately with { isReady: bool }.
    fileSystemSearchPageLoad()
      .then((response) => {
        if (response.isReady) {
          isIndexing.value = false
        }
        // If not ready: stay true. C# will push fileSystemIndexingStatus when done.
      })
      .catch(() => {
        isIndexing.value = false // bridge unavailable — let search try anyway
      })
  })

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

    // Not ready yet — wait for isIndexing watcher to retry.
    if (isIndexing.value) {
      searching.value = false
      return
    }

    searching.value = true

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
      if (thisGeneration === generation) searching.value = false
    }
  }

  watch(debouncedQuery, (rawQuery) => runSearch(rawQuery ?? ''), { immediate: true })

  // When isIndexing clears, re-run any query that was blocked.
  watch(isIndexing, (nowIndexing) => {
    if (!nowIndexing && (debouncedQuery.value ?? '').trim()) {
      runSearch(debouncedQuery.value ?? '')
    }
  })

  return { results, searching, isIndexing, totalCount, errorMessage }
}
