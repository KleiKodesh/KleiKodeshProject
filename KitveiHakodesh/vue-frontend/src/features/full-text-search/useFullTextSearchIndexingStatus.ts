import { ref, onMounted } from 'vue'
import { serviceStream } from '@/webview-host/serviceClient'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import { reportSearchIndexStatus, type SearchIndexStatus } from '@/composables/useSearchIndexStatus'

export interface IndexingState {
  isReady: boolean
  isIndexing: boolean
  percentage: number
  processedChunks: number
  totalChunks: number
  dbNotFound: boolean
  /** The build stopped on an error — the index will not become ready on its own. */
  buildFailed: boolean
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
  buildFailed: false,
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

function fullTextSearchIndexStatusOf(frame: IndexStatusFrame): SearchIndexStatus {
  if (frame.dbMissing) return { kind: 'unavailable', reason: 'dbMissing' }
  if (frame.failed) return { kind: 'unavailable', reason: 'buildFailed' }
  if (frame.isIndexing && !frame.isReady) return { kind: 'building', progressPercent: frame.percentage ?? null }
  return { kind: 'ready' }
}

/**
 * Live FTS index build state, ONE app-wide subscription. The KitveiHakodesh service PUSHES
 * a status frame on every build-progress change over one open stream
 * (`ftsIndexProgressStream`) — no polling. The stream ends on its own when the build reaches
 * a terminal state (ready, failed, or no DB). If it drops early (the service restarting after
 * a database-path change), it is reopened once the service is back. Same path in dev and in
 * the hosted app.
 *
 * App-wide so a failed build reaches the shared index status (and its toast) even when the
 * full-text-search page is not open. `watchFullTextSearchIndexStatus` is idempotent: the app
 * shell calls it once at startup and the page calls it on mount, which reopens a stream that
 * already ended (a rebuild started later from Settings) without ever running two.
 */
const sharedIndexingState = ref<IndexingState>({ ...IDLE })
let isWatching = false

export function watchFullTextSearchIndexStatus(): void {
  if (isWatching) return
  isWatching = true
  const cache = useSearchCacheStore()
  const consume = async () => {
    // A rebuild that starts while we watch means the index was found stale or corrupt.
    // Every cached search result is from the old index, so purge the cache once per
    // ready -> indexing transition rather than serve stale hits on the next search.
    let wasReady = false
    for (;;) {
      try {
        const stream = serviceStream<IndexStatusFrame>('ftsIndexProgressStream', {})
        let last: IndexStatusFrame | null = null
        for await (const s of stream) {
          last = s
          reportSearchIndexStatus('fullTextSearch', fullTextSearchIndexStatusOf(s))
          if (wasReady && s.isIndexing) cache.clear().catch(() => {/* non-fatal */})
          wasReady = s.isReady && !s.isIndexing
          sharedIndexingState.value = {
            isReady: s.isReady,
            isIndexing: s.isIndexing,
            percentage: s.percentage ?? 0,
            processedChunks: s.processedChunks ?? 0,
            totalChunks: s.totalChunks ?? 0,
            dbNotFound: s.dbMissing ?? false,
            buildFailed: s.failed ?? false,
            isMerging: s.isMerging ?? false,
            mergePassesDone: s.mergePassesDone ?? 0,
            mergePassesExpected: s.mergePassesExpected ?? 0,
          }
        }
        // Terminal state reached — nothing more will be pushed until a new build starts.
        if (last && (last.dbMissing || last.failed || (last.isReady && !last.isIndexing))) return
      } catch {
        /* service restarting — fall through to the reconnect delay */
      }
      // Stream dropped before a terminal state (service restart) — reconnect.
      await new Promise((r) => setTimeout(r, 1500))
    }
  }
  void consume().finally(() => { isWatching = false })
}

export function useFullTextSearchIndexingStatus() {
  onMounted(watchFullTextSearchIndexStatus)
  return { state: sharedIndexingState }
}
