import { computed, reactive, watch, type Ref } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { catalogTocIndexStatus, localFileIndexStatus, type LocalFileIndexStatus } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'

/**
 * ONE place that knows where each search index stands — ready, being built, or
 * unavailable — so the home/address-bar dropdown and each index's own page say the
 * same thing instead of spinning forever.
 *
 * The search code that talks to an index REPORTS what it learned
 * (`reportSearchIndexStatus`); the UI READS it (`useSearchIndexStatus`). A switch
 * into "unavailable" also raises the app toast once, so the user hears about a
 * failure even when the page that owns the index is not open.
 *
 * While the catalog or file index is building, its status endpoint is polled in the
 * background (see BACKGROUND POLLING below), so a build that fails is reported
 * promptly — not only on the user's next search.
 */

export type SearchIndexName = 'catalogToc' | 'localFiles' | 'fullTextSearch'

export type SearchIndexUnavailableReason = 'dbMissing' | 'buildFailed' | 'buildFailedRetrying' | 'serviceError'

export type SearchIndexStatus =
  | { kind: 'ready' }
  /** `progressPercent` is null when the index reports no measurable progress. */
  | { kind: 'building'; progressPercent: number | null }
  | { kind: 'unavailable'; reason: SearchIndexUnavailableReason }

const READY: SearchIndexStatus = { kind: 'ready' }

const statusByIndex = reactive<Record<SearchIndexName, SearchIndexStatus>>({
  catalogToc: READY,
  localFiles: READY,
  fullTextSearch: READY,
})

const INDEX_DISPLAY_NAMES: Record<SearchIndexName, string> = {
  catalogToc: t('composables.index.catalogToc'),
  localFiles: t('composables.index.localFiles'),
  fullTextSearch: t('composables.index.fullTextSearch'),
}

/** The Settings > reset button that rebuilds each index (labels from SettingsPageResetSection). */
export const INDEX_REBUILD_BUTTON_LABELS: Record<SearchIndexName, string> = {
  catalogToc: t('composables.index.rebuildCatalogToc'),
  localFiles: t('composables.index.rebuildLocalFiles'),
  fullTextSearch: t('composables.index.rebuildFullTextSearch'),
}

function unavailableReasonText(index: SearchIndexName, reason: SearchIndexUnavailableReason): string {
  switch (reason) {
    case 'dbMissing':
      return t('composables.index.dbMissing')
    case 'buildFailed':
      return t('composables.index.buildFailed', { button: INDEX_REBUILD_BUTTON_LABELS[index] })
    case 'buildFailedRetrying':
      return t('composables.index.buildFailedRetrying')
    case 'serviceError':
      return t('composables.index.serviceError')
  }
}

export function reportSearchIndexStatus(index: SearchIndexName, status: SearchIndexStatus): void {
  const previous = statusByIndex[index]
  statusByIndex[index] = status
  if (status.kind === 'unavailable' && previous.kind !== 'unavailable') {
    showToast(describeSearchIndexStatus(index, status)!, { variant: 'error', duration: 6000 })
  }
}

/** UI-language one-liner for a non-ready status; null when the index is ready. */
export function describeSearchIndexStatus(index: SearchIndexName, status: SearchIndexStatus): string | null {
  if (status.kind === 'ready') return null
  if (status.kind === 'building') {
    const progress = status.progressPercent === null ? '' : ` (${Math.round(status.progressPercent)}%)`
    return t('composables.index.building', { progress })
  }
  return t('composables.index.unavailable', { name: INDEX_DISPLAY_NAMES[index], reason: unavailableReasonText(index, status.reason) })
}

/** Reactive status of one index plus its display text (null while ready). */
export function useSearchIndexStatus(index: SearchIndexName) {
  const status = computed(() => statusByIndex[index])
  const statusText = computed(() => describeSearchIndexStatus(index, status.value))
  const isUnavailable = computed(() => status.value.kind === 'unavailable')
  return { status, statusText, isUnavailable }
}

// ── Catalog TOC index ─────────────────────────────────────────────────────────

/** The catalog fields both `catalogTocSearch` and `catalogTocStatus` carry. */
interface CatalogTocIndexState {
  ready: boolean
  superseded?: boolean
  error?: string | null
  dbMissing?: boolean
  builtBooks?: number
  totalBooks?: number
  buildFailure?: string | null
  retryingAfterBuildFailure?: boolean
}

/**
 * Report what one catalog reply says about the catalog index.
 * A missing DB or a failed build (none retrying) is terminal: the caller stops retrying.
 */
export function reportCatalogTocSearchResponse(response: CatalogTocIndexState): void {
  if (response.error || response.buildFailure) {
    reportSearchIndexStatus('catalogToc', { kind: 'unavailable', reason: 'buildFailed' })
  } else if (response.dbMissing) {
    reportSearchIndexStatus('catalogToc', { kind: 'unavailable', reason: 'dbMissing' })
  } else if (response.retryingAfterBuildFailure && !response.ready) {
    // Keep saying "failed, retrying" — flipping back to plain "building" would hide the failure.
    reportSearchIndexStatus('catalogToc', { kind: 'unavailable', reason: 'buildFailedRetrying' })
  } else if (!response.ready) {
    const total = response.totalBooks ?? 0
    reportSearchIndexStatus('catalogToc', {
      kind: 'building',
      progressPercent: total > 0 ? ((response.builtBooks ?? 0) / total) * 100 : null,
    })
  } else if (!response.superseded) {
    reportSearchIndexStatus('catalogToc', READY)
  }
}

// ── Local file index ──────────────────────────────────────────────────────────

export function reportLocalFileIndexState(result: LocalFileIndexStatus): void {
  if (result.isBuilding) {
    const percent = result.progressPercent ?? -1
    reportSearchIndexStatus('localFiles', { kind: 'building', progressPercent: percent >= 0 ? percent : null })
  } else if (result.buildFailure) {
    reportSearchIndexStatus('localFiles', { kind: 'unavailable', reason: 'buildFailed' })
  }
}

/**
 * A file search failed: ask the service whether the index build is what failed, so the
 * user is told "build failed" rather than a generic service error.
 */
export async function reportLocalFileSearchFailure(): Promise<void> {
  const result = await localFileIndexStatus()
  reportSearchIndexStatus('localFiles', {
    kind: 'unavailable',
    reason: result.buildFailure ? 'buildFailed' : 'serviceError',
  })
}

const LOCAL_FILE_INDEX_POLL_MS = 1000

/**
 * While a file search is waiting, ask the service whether the file index is being
 * built and report it — the file search call itself just blocks until the build ends.
 * Stops (and reports ready) as soon as nobody is waiting.
 */
export function useLocalFileIndexBuildPolling(isWaiting: Ref<boolean>): void {
  let isRequestInFlight = false
  const { pause, resume } = useIntervalFn(
    async () => {
      if (isRequestInFlight) return
      isRequestInFlight = true
      try {
        const result = await localFileIndexStatus()
        if (!isWaiting.value || result.unreachable) return
        reportLocalFileIndexState(result)
      } finally {
        isRequestInFlight = false
      }
    },
    LOCAL_FILE_INDEX_POLL_MS,
    { immediate: false, immediateCallback: true },
  )
  watch(isWaiting, (waiting) => {
    if (waiting) {
      resume()
      return
    }
    // Leave a 'building' status as is: the build may still run, and the background
    // poller below keeps watching it until it settles.
    pause()
  })
}

// ── BACKGROUND POLLING ────────────────────────────────────────────────────────
//
// App-wide, module-level: while the catalog or file index is known to be building
// (or retrying after a failure), poll its status endpoint until it settles. This is
// what makes a failed build reach the toast without the user searching again. Idle
// otherwise — no traffic while every index is ready or terminally unavailable.
// (The full-text-search index needs none of this: the service pushes its frames.)

const BACKGROUND_STATUS_POLL_MS = 2000

function isInFlight(status: SearchIndexStatus): boolean {
  return status.kind === 'building' || (status.kind === 'unavailable' && status.reason === 'buildFailedRetrying')
}

// Skips a tick while the previous one is still waiting, so replies never overlap or land
// out of order. A reply is also dropped when the index's status was reported by someone
// else (a search reply) since the request went out — that report is newer.
let isBackgroundPollInFlight = false

const backgroundStatusPolling = useIntervalFn(
  async () => {
    if (isBackgroundPollInFlight) return
    isBackgroundPollInFlight = true
    try {
      if (isInFlight(statusByIndex.catalogToc)) {
        const before = statusByIndex.catalogToc
        const catalog = await catalogTocIndexStatus()
        if (catalog && statusByIndex.catalogToc === before) reportCatalogTocSearchResponse(catalog)
      }
      if (isInFlight(statusByIndex.localFiles)) {
        const before = statusByIndex.localFiles
        const files = await localFileIndexStatus()
        // Unreachable = nothing known (service restarting): keep the status, try next tick.
        if (!files.unreachable && statusByIndex.localFiles === before) {
          if (files.isBuilding || files.buildFailure) reportLocalFileIndexState(files)
          else reportSearchIndexStatus('localFiles', READY)
        }
      }
    } finally {
      isBackgroundPollInFlight = false
    }
  },
  BACKGROUND_STATUS_POLL_MS,
  { immediate: false },
)

watch(
  () => isInFlight(statusByIndex.catalogToc) || isInFlight(statusByIndex.localFiles),
  (anyInFlight) => (anyInFlight ? backgroundStatusPolling.resume() : backgroundStatusPolling.pause()),
)
