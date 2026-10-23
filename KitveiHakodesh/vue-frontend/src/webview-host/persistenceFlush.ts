import { nextTick } from 'vue'

/**
 * Everything that still owes a persistence write, so it can all be written at once
 * when the host is about to close its window.
 *
 * A browser tab closing fires visibilitychange / pagehide / beforeunload, and
 * installBrowserPersistenceFlush below runs the flush on those. The WinForms WebView2
 * hosts (the demo app, the Word task pane) close their window and dispose the
 * WebView2 WITHOUT firing any of them, so a write still sitting in its trailing timer
 * was lost. The host now calls `window.__khsFlushPersistence()` first and waits for
 * the `persistenceFlushed` message before disposing (AppViewer.FlushPersistenceAsync).
 *
 * Two phases, because they depend on each other:
 *   1. position captures - "write where the reader is into the stores now" (a book
 *      view's savePos). These mutate store state, and the watchers that persist that
 *      state run on Vue's scheduler, so the flush waits one tick for them to schedule
 *      their coalesced writes...
 *   2. pending writers - every useCoalescedWrite instance, flushed whether or not its
 *      timer is pending (a flush with nothing pending is a no-op).
 *
 * localStorage writes land synchronously; IndexedDB puts resolve later, and the
 * returned promise settles when the last of them has - that is what the host waits for.
 */

type Flusher = () => void | Promise<unknown>

// Capture -> the tab whose view owns it (undefined for a capture that is not per tab).
const positionCaptures = new Map<Flusher, string | undefined>()
const pendingWriters = new Set<Flusher>()

/** Phase 1: a save that writes the current reading position into the stores. `tabId`
 *  lets a tab close ask for just its own view's position (capturePositionsForTab). */
export function registerPositionCapture(capture: Flusher, tabId?: string): () => void {
  positionCaptures.set(capture, tabId)
  return () => { positionCaptures.delete(capture) }
}

/** Phase 2: a coalesced writer whose pending write must go out before the page dies. */
export function registerPendingWriter(flush: Flusher): () => void {
  pendingWriters.add(flush)
  return () => { pendingWriters.delete(flush) }
}

/**
 * Phase 1 for ONE tab, run by the tab store as it closes the tab. The view is still
 * mounted at that point; its own unmount save comes after the tab has left the list,
 * when the store can no longer fold the position into the tab's recents entry - so
 * without this, a recents row reopened after a close landed where the reader was at
 * the previous save, not where they closed the tab.
 */
export function capturePositionsForTab(tabId: string): void {
  for (const [capture, owner] of positionCaptures) {
    if (owner !== tabId) continue
    try { void capture() } catch { /* one broken saver must not stop the close */ }
  }
}

function runAll(set: Iterable<Flusher>, into: Promise<unknown>[]): void {
  for (const fn of set) {
    try {
      const result = fn()
      // A rejected write must not hide the others from the host's ack.
      if (result) into.push(result.catch(() => undefined))
    } catch {
      // One broken saver must not stop the rest from flushing.
    }
  }
}

/**
 * The browser's own leave signals - the tab hidden, closed or reloaded - run the same
 * two phases, from ONE place and in that order. Each writer used to flush itself on
 * these events, but listener order is registration order: the stores register at
 * boot, the book view that captures the position only when it mounts, so the recents
 * list was written before the position had been folded into it, and the write the
 * capture then scheduled never ran. Installed once, before any store or view exists.
 */
let browserFlushInstalled = false
export function installBrowserPersistenceFlush(): void {
  if (browserFlushInstalled) return
  browserFlushInstalled = true
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') void flushAllPersistence()
  })
  window.addEventListener('pagehide', () => { void flushAllPersistence() })
  window.addEventListener('beforeunload', () => { void flushAllPersistence() })
}

/** Runs both phases; resolves when every write they issued has settled. */
export async function flushAllPersistence(): Promise<void> {
  const writes: Promise<unknown>[] = []
  runAll(positionCaptures.keys(), writes)
  // Let the store watchers the captures dirtied schedule their coalesced writes.
  await nextTick()
  runAll(pendingWriters, writes)
  await Promise.all(writes)
}

declare global {
  interface Window {
    /**
     * Called by the C# host (AppViewer.FlushPersistenceAsync) right before it closes
     * its window. Returns 'pending' synchronously; the page posts
     * `{ action: 'persistenceFlushed' }` once every write has settled.
     */
    __khsFlushPersistence?: () => 'pending'
  }
}

/** Installs the host entry point. Call once at startup when a C# host is present. */
export function installHostPersistenceFlush(): void {
  window.__khsFlushPersistence = () => {
    void flushAllPersistence().finally(() => {
      window.chrome?.webview?.postMessage({ id: '0', action: 'persistenceFlushed' })
    })
    return 'pending'
  }
}
