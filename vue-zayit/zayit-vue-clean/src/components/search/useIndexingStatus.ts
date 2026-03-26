/**
 * Tracks bloom filter indexing state.
 *
 * On mount: polls C# once for current state (handles the case where indexing
 * started before the Vue page opened).
 * While indexing: receives live progress via the __onWebviewEvent push bus
 * (C# calls window.__onWebviewEvent({ event: 'bloomIndexProgress', ... })).
 *
 * In dev mode (no C# host): simulates a short indexing sequence so the UI
 * can be developed and tested without a running C# backend.
 */
import { ref, onMounted, onUnmounted } from 'vue'
import { isHosted, onWebviewEvent } from '@/host/db'

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

export function useIndexingStatus() {
  const state = ref<IndexingState>({
    isReady: false,
    isIndexing: false,
    percentage: 0,
    processedChunks: 0,
    totalChunks: 0,
    eta: '',
  })

  let unregister: (() => void) | null = null
  let devTimer: ReturnType<typeof setTimeout> | null = null

  async function poll() {
    if (!isHosted) return
    try {
      const progress = await callAction<{
        isReady: boolean
        isIndexing: boolean
        percentage: number
        processedChunks: number
        totalChunks: number
        eta: string
      }>('GetBloomIndexingProgress')

      if (progress) {
        state.value = {
          isReady: progress.isReady,
          isIndexing: progress.isIndexing,
          percentage: progress.percentage ?? 0,
          processedChunks: progress.processedChunks ?? 0,
          totalChunks: progress.totalChunks ?? 0,
          eta: progress.eta ?? '',
        }
      }
    } catch (err) {
      console.warn('[useIndexingStatus] poll failed:', err)
    }
  }

  function startDevSimulation() {
    // Simulate indexing: 0 → 100% over ~3 seconds so the overlay is visible in dev
    state.value = { isReady: false, isIndexing: true, percentage: 0, processedChunks: 0, totalChunks: 100, eta: '3s' }
    let pct = 0
    const tick = () => {
      pct += 10
      if (pct >= 100) {
        state.value = { isReady: true, isIndexing: false, percentage: 100, processedChunks: 100, totalChunks: 100, eta: '' }
        return
      }
      state.value = { isReady: false, isIndexing: true, percentage: pct, processedChunks: pct, totalChunks: 100, eta: `${Math.round((100 - pct) / 10 * 0.3)}s` }
      devTimer = setTimeout(tick, 300)
    }
    devTimer = setTimeout(tick, 300)
  }

  onMounted(async () => {
    if (!isHosted) {
      startDevSimulation()
      return
    }

    // Initial poll to get current state
    await poll()

    // Subscribe to live push events from C#
    unregister = onWebviewEvent((msg) => {
      if (msg.event !== 'bloomIndexProgress') return
      state.value = {
        isReady: msg.isReady as boolean,
        isIndexing: !(msg.isReady as boolean),
        percentage: msg.percentage as number,
        processedChunks: msg.processedChunks as number,
        totalChunks: msg.totalChunks as number,
        eta: (msg.eta as string) ?? '',
      }
    })
  })

  onUnmounted(() => {
    unregister?.()
    unregister = null
    if (devTimer) { clearTimeout(devTimer); devTimer = null }
  })

  return { state }
}
