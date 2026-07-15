import { ref, onMounted, onUnmounted } from 'vue'
import { isHosted, onWebviewEvent } from '@/webview-host/seforimDb'
import { callBridgeAction } from '@/webview-host/bridge'
import { serviceCall } from '@/webview-host/serviceClient'
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
  let devTimer: ReturnType<typeof setTimeout> | null = null

  onMounted(async () => {
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      // Dev: poll the KitveiHakodesh service, which builds the FTS index in the
      // background. Keep polling while the build is running; stop once it's done.
      const poll = async () => {
        try {
          const s = await serviceCall<{
            isReady: boolean; isIndexing: boolean; percentage: number
            processedChunks: number; totalChunks: number
          }>('ftsIndexingStatus')
          state.value = {
            isReady: s.isReady,
            isIndexing: s.isIndexing,
            percentage: s.percentage ?? 0,
            processedChunks: s.processedChunks ?? 0,
            totalChunks: s.totalChunks ?? 0,
            eta: '',
            segmentCount: 0,
            latestSegmentPct: null,
            dbNotFound: false,
          }
          if (s.isIndexing || !s.isReady) devTimer = setTimeout(poll, 1000)
        } catch {
          devTimer = setTimeout(poll, 2000)
        }
      }
      void poll()
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
    unregister?.()
    if (devTimer) clearTimeout(devTimer)
  })

  return { state }
}
