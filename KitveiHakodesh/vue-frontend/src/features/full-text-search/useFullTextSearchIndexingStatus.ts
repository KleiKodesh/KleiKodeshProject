import { ref, onMounted, onUnmounted } from 'vue'
import { hasHostBridge, onWebviewEvent } from '@/webview-host/seforimDb'
import { callBridgeAction } from '@/webview-host/bridge'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'

export interface IndexingState {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  eta: string
  segmentCount: number
  latestSegmentPct: number | null
  dbNotFound: boolean
}

const IDLE: IndexingState = {
  isReady: false,
  isIndexing: false,
  percentage: 0,
  processedChunks: 0,
  totalChunks: 0,
  eta: '',
  segmentCount: 0,
  latestSegmentPct: null,
  dbNotFound: false,
}

export function useFullTextSearchIndexingStatus() {
  const state = ref<IndexingState>({ ...IDLE })
  const cache = useSearchCacheStore()
  let unregister: (() => void) | null = null
  let devAbort: AbortController | null = null
  // onMounted's body awaits, so the component can unmount while it is still running —
  // before `unregister` has been assigned. onUnmounted would then find nothing to undo
  // and the listener would stay registered for good, writing into a dead component's
  // state on every progress frame. This flag makes the late registration self-cancel.
  let unmounted = false

  onMounted(async () => {
    if (!hasHostBridge) {
      // Dev: the service PUSHES a status frame on every build-progress change over one
      // open stream (ftsIndexProgressStream) — no polling. The stream ends on its own
      // when the build reaches a terminal state (ready, or no DB). If it drops early
      // (service restart), reopen it once the service is back.
      const abort = new AbortController()
      devAbort = abort
      const consume = async () => {
        while (!abort.signal.aborted) {
          try {
            const stream = serviceStream<{
              isReady: boolean; isIndexing: boolean; percentage: number
              processedChunks: number; totalChunks: number; dbMissing: boolean
            }>('ftsIndexProgressStream', {}, abort.signal)
            let last: { isReady: boolean; isIndexing: boolean; dbMissing: boolean } | null = null
            for await (const s of stream) {
              last = s
              state.value = {
                isReady: s.isReady,
                isIndexing: s.isIndexing,
                percentage: s.percentage ?? 0,
                processedChunks: s.processedChunks ?? 0,
                totalChunks: s.totalChunks ?? 0,
                eta: '',
                segmentCount: 0,
                latestSegmentPct: null,
                dbNotFound: s.dbMissing ?? false,
              }
            }
            // Terminal state reached — nothing more will ever be pushed.
            if (last && (last.dbMissing || (last.isReady && !last.isIndexing))) return
          } catch {
            /* service restarting — fall through to the reconnect delay */
          }
          // Stream dropped before a terminal state (service restart) — reconnect.
          await new Promise((r) => setTimeout(r, 1500))
        }
      }
      void consume()
      return
    }

    try {
      const p = await callBridgeAction<IndexingState>('GetFtsIndexingProgress')
      if (p)
        state.value = {
          isReady: p.isReady,
          isIndexing: p.isIndexing,
          percentage: p.percentage ?? 0,
          processedChunks: p.processedChunks ?? 0,
          totalChunks: p.totalChunks ?? 0,
          eta: p.eta ?? '',
          segmentCount: p.segmentCount ?? 0,
          latestSegmentPct: p.latestSegmentPct ?? null,
          dbNotFound: p.dbNotFound ?? false,
        }
    } catch (err) {
      console.warn('[useFullTextSearchIndexingStatus] poll failed:', err)
    }

    if (unmounted) return

    unregister = onWebviewEvent((msg) => {
      if (msg.event === 'ftsDbNotFound') {
        state.value = { ...IDLE, dbNotFound: true }
        return
      }
      if (msg.event === 'ftsIndexInvalidated') {
        // Old or corrupt index detected — rebuild started automatically.
        // All cached search results are from the old index and must be purged,
        // otherwise stale or corrupt results would be served on the next search.
        console.warn('[useFullTextSearchIndexingStatus] FTS index invalidated:', msg.reason)
        cache.clear().catch(() => {/* non-fatal */})
        state.value = { ...IDLE, isIndexing: true, totalChunks: 0, eta: '', segmentCount: 0, latestSegmentPct: null }
        return
      }
      if (msg.event !== 'ftsIndexProgress') return
      state.value = {
        isReady: msg.isReady as boolean,
        isIndexing: msg.isIndexing as boolean,
        percentage: msg.percentage as number,
        processedChunks: msg.processedChunks as number,
        totalChunks: msg.totalChunks as number,
        eta: (msg.eta as string) ?? '',
        segmentCount: (msg.segmentCount as number) ?? 0,
        latestSegmentPct: (msg.latestSegmentPct as number | null) ?? null,
        dbNotFound: false,
      }
    })
  })

  onUnmounted(() => {
    unmounted = true
    unregister?.()
    unregister = null
    devAbort?.abort()
  })

  return { state }
}
