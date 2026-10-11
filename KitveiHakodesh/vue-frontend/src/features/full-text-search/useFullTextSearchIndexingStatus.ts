import { ref, onMounted, onUnmounted } from 'vue'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'

export interface IndexingState {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  dbNotFound: boolean
  isMerging: boolean
  mergePassesDone: number
  mergePassesExpected: number
}

const IDLE: IndexingState = {
  isReady: false,
  isIndexing: false,
  percentage: 0,
  processedChunks: 0,
  totalChunks: 0,
  dbNotFound: false,
  isMerging: false,
  mergePassesDone: 0,
  mergePassesExpected: 0,
}

/** One frame of the `ftsIndexProgressStream` op (Core FtsIndexStatus, camelCased). */
interface IndexStatusFrame {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  dbMissing: boolean
  failed?: boolean
  isMerging: boolean
  mergePassesDone: number
  mergePassesExpected: number
}

/**
 * Live FTS index build state. The KitveiHakodesh service PUSHES a status frame on every
 * build-progress change over one open stream (`ftsIndexProgressStream`) — no polling. The
 * stream ends on its own when the build reaches a terminal state (ready, or no DB). If it
 * drops early (the service restarting after a database-path change), it is reopened once
 * the service is back. Same path in dev and in the hosted app.
 */
export function useFullTextSearchIndexingStatus() {
  const state = ref<IndexingState>({ ...IDLE })
  const cache = useSearchCacheStore()
  let abort: AbortController | null = null

  onMounted(() => {
    const controller = new AbortController()
    abort = controller
    const consume = async () => {
      // A rebuild that starts while we watch means the index was found stale or corrupt.
      // Every cached search result is from the old index, so purge the cache once per
      // ready -> indexing transition rather than serve stale hits on the next search.
      let wasReady = false
      while (!controller.signal.aborted) {
        try {
          const stream = serviceStream<IndexStatusFrame>('ftsIndexProgressStream', {}, controller.signal)
          let last: IndexStatusFrame | null = null
          for await (const s of stream) {
            last = s
            if (wasReady && s.isIndexing) cache.clear().catch(() => {/* non-fatal */})
            wasReady = s.isReady && !s.isIndexing
            state.value = {
              isReady: s.isReady,
              isIndexing: s.isIndexing,
              percentage: s.percentage ?? 0,
              processedChunks: s.processedChunks ?? 0,
              totalChunks: s.totalChunks ?? 0,
              dbNotFound: s.dbMissing ?? false,
              isMerging: s.isMerging ?? false,
              mergePassesDone: s.mergePassesDone ?? 0,
              mergePassesExpected: s.mergePassesExpected ?? 0,
            }
          }
          // Terminal state reached — nothing more will ever be pushed.
          if (last && (last.dbMissing || last.failed || (last.isReady && !last.isIndexing))) return
        } catch {
          /* service restarting — fall through to the reconnect delay */
        }
        // Stream dropped before a terminal state (service restart) — reconnect.
        await new Promise((r) => setTimeout(r, 1500))
      }
    }
    void consume()
  })

  onUnmounted(() => {
    abort?.abort()
    abort = null
  })

  return { state }
}
