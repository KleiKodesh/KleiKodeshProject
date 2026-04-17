import { ref, onMounted, onUnmounted } from 'vue'
import { isHosted, onWebviewEvent } from '@/host/seforimDb'

export interface IndexingState {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  eta: string
}

function callAction<T>(name: string, ...params: unknown[]): Promise<T> {
  if (typeof window.__webviewAction !== 'function')
    return Promise.reject(new Error('bridge not available'))
  return window.__webviewAction(name, params as unknown as object) as Promise<T>
}

const IDLE: IndexingState = {
  isReady: false,
  isIndexing: false,
  percentage: 0,
  processedChunks: 0,
  totalChunks: 0,
  eta: '',
}

export function useIndexingStatus() {
  const state = ref<IndexingState>({ ...IDLE })
  let unregister: (() => void) | null = null
  let devTimer: ReturnType<typeof setTimeout> | null = null

  onMounted(async () => {
    if (!isHosted || typeof window.__webviewAction !== 'function') {
      // Dev simulation: 0→100% over ~3s
      state.value = { ...IDLE, isIndexing: true, totalChunks: 100, eta: '3s' }
      let pct = 0
      const tick = () => {
        pct += 10
        if (pct >= 100) {
          state.value = {
            isReady: true,
            isIndexing: false,
            percentage: 100,
            processedChunks: 100,
            totalChunks: 100,
            eta: '',
          }
          return
        }
        state.value = {
          isReady: false,
          isIndexing: true,
          percentage: pct,
          processedChunks: pct,
          totalChunks: 100,
          eta: `${Math.round((100 - pct) * 0.03)}s`,
        }
        devTimer = setTimeout(tick, 300)
      }
      devTimer = setTimeout(tick, 300)
      return
    }

    try {
      const p = await callAction<IndexingState>('GetBloomIndexingProgress')
      if (p)
        state.value = {
          isReady: p.isReady,
          isIndexing: p.isIndexing,
          percentage: p.percentage ?? 0,
          processedChunks: p.processedChunks ?? 0,
          totalChunks: p.totalChunks ?? 0,
          eta: p.eta ?? '',
        }
    } catch (err) {
      console.warn('[useIndexingStatus] poll failed:', err)
    }

    unregister = onWebviewEvent((msg) => {
      if (msg.event === 'bloomIndexVersionMismatch') {
        const oldVer = msg.oldVersion as string
        const newVer = msg.newVersion as string
        const rebuild = window.confirm(
          `הגרסה של האפליקציה עודכנה (${oldVer} ← ${newVer}).\nהאם לבנות מחדש את אינדקס החיפוש?`,
        )
        callAction('ConfirmReindex', { confirm: rebuild }).catch(() => {})
        return
      }
      if (msg.event === 'bloomIndexInvalidated') {
        // Old or corrupt index format detected — rebuild started automatically, nothing to confirm.
        console.warn('[useIndexingStatus] Bloom index invalidated:', msg.reason)
        state.value = { ...IDLE, isIndexing: true, totalChunks: 0, eta: '' }
        return
      }
      if (msg.event !== 'bloomIndexProgress') return
      state.value = {
        isReady: msg.isReady as boolean,
        isIndexing: msg.isIndexing as boolean,
        percentage: msg.percentage as number,
        processedChunks: msg.processedChunks as number,
        totalChunks: msg.totalChunks as number,
        eta: (msg.eta as string) ?? '',
      }
    })
  })

  onUnmounted(() => {
    unregister?.()
    if (devTimer) clearTimeout(devTimer)
  })

  return { state }
}
