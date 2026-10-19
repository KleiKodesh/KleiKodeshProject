import { useIntervalFn } from '@vueuse/core'
import { serviceCall } from '@/webview-host/serviceClient'

/** One index's last build reason (Core IndexBuildReason, camelCased). */
interface IndexBuildReason {
  reason: string
  atUtc: string | Date
}

/** The `indexBuildReasons` op (Core IndexBuildReasons, camelCased). */
interface IndexBuildReasons {
  fullTextSearch?: IndexBuildReason | null
  catalogToc?: IndexBuildReason | null
  localFiles?: IndexBuildReason | null
}

const POLL_MS = 15_000

/**
 * Writes why each service index (full-text, catalog, local files) last started building to the
 * console, once per new reason — so a rebuild nobody asked for can be explained from DevTools.
 * Polls a read-only housekeeping op: it never starts a build and does not count as activity.
 */
export function useIndexBuildReasonLog(): void {
  const seen = new Map<string, string>()

  async function poll(): Promise<void> {
    let reasons: IndexBuildReasons
    try {
      reasons = await serviceCall<IndexBuildReasons>('indexBuildReasons')
    } catch {
      return // service down or an older service without the op — nothing to report
    }
    for (const [index, entry] of Object.entries(reasons)) {
      if (!entry) continue
      const key = `${String(entry.atUtc)}|${entry.reason}`
      if (seen.get(index) === key) continue
      seen.set(index, key)
      console.info(`[index build] ${index}: ${entry.reason} (at ${new Date(entry.atUtc).toLocaleTimeString()})`)
    }
  }

  void poll()
  useIntervalFn(poll, POLL_MS)
}
