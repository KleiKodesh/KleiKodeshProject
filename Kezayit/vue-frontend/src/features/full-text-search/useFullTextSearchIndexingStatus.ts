import { ref, onMounted, onUnmounted } from 'vue'
import { isHosted, onWebviewEvent } from '@/webview-host/seforimDb'
import { callBridgeAction } from '@/webview-host/bridge'

export interface IndexingState {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  eta: string
  segmentCount: number
  latestSegmentPct: number | null
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
}

export function useFullTextSearchIndexingStatus() {
  const state = ref<IndexingState>({ ...IDLE })
  let unregister: (() => void) | null = null
  let devTimer: ReturnType<typeof setTimeout> | null = null

  onMounted(async () => {
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      // Dev simulation: 0→100% over ~3s, ticking every 10,000 lines out of 1,000,000
      const TOTAL_LINES = 1_000_000
      const TICK_LINES  = 10_000
      state.value = { ...IDLE, isIndexing: true, totalChunks: TOTAL_LINES, eta: '3s', segmentCount: 0, latestSegmentPct: null }
      let processed = 0
      let devLatestSegmentPct: number | null = null
      const tick = () => {
        processed += TICK_LINES
        const pct = Math.min(100, (processed / TOTAL_LINES) * 100)
        if (pct >= 100) {
          state.value = {
            isReady: true,
            isIndexing: false,
            percentage: 100,
            processedChunks: TOTAL_LINES,
            totalChunks: TOTAL_LINES,
            eta: '',
            segmentCount: devLatestSegmentPct !== null ? 2 : 0,
            latestSegmentPct: devLatestSegmentPct,
          }
          return
        }
        // Simulate a segment flush at ~20% and ~60%
        if (pct >= 20 && devLatestSegmentPct === null) devLatestSegmentPct = pct
        if (pct >= 60 && devLatestSegmentPct !== null && devLatestSegmentPct < 60) devLatestSegmentPct = pct
        state.value = {
          isReady: pct >= 20,
          isIndexing: true,
          percentage: pct,
          processedChunks: processed,
          totalChunks: TOTAL_LINES,
          eta: `${Math.round((TOTAL_LINES - processed) / TOTAL_LINES * 3)}s`,
          segmentCount: devLatestSegmentPct !== null ? (pct >= 60 ? 2 : 1) : 0,
          latestSegmentPct: devLatestSegmentPct,
        }
        devTimer = setTimeout(tick, 300)
      }
      devTimer = setTimeout(tick, 300)
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
        }
    } catch (err) {
      console.warn('[useFullTextSearchIndexingStatus] poll failed:', err)
    }

    unregister = onWebviewEvent((msg) => {
      if (msg.event === 'ftsIndexVersionMismatch') {
        const oldVersion = msg.oldVersion as string
        const newVersion = msg.newVersion as string
        const rebuild = window.confirm(
          `הגרסה של האפליקציה עודכנה (${oldVersion} ← ${newVersion}).\nהאם לבנות מחדש את אינדקס החיפוש?`,
        )
        callBridgeAction('FtsConfirmReindex', { confirm: rebuild }).catch(() => {})
        return
      }
      if (msg.event === 'ftsIndexInvalidated') {
        // Old or corrupt index detected — rebuild started automatically, nothing to confirm.
        console.warn('[useFullTextSearchIndexingStatus] FTS index invalidated:', msg.reason)
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
      }
    })
  })

  onUnmounted(() => {
    unregister?.()
    if (devTimer) clearTimeout(devTimer)
  })

  return { state }
}
