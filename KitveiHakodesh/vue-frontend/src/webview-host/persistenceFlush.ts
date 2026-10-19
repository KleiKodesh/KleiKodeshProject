import { nextTick } from 'vue'

/**
 * Everything that still owes a persistence write, so it can all be written at once
 * when the host is about to close its window.
 *
 * A browser tab closing fires visibilitychange / pagehide / beforeunload, and each
 * coalesced write and position save flushes itself on those. The WinForms WebView2
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

const positionCaptures = new Set<Flusher>()
const pendingWriters = new Set<Flusher>()

function register(set: Set<Flusher>, fn: Flusher): () => void {
  set.add(fn)
  return () => { set.delete(fn) }
}

/** Phase 1: a save that writes the current reading position into the stores. */
export function registerPositionCapture(capture: Flusher): () => void {
  return register(positionCaptures, capture)
}

/** Phase 2: a coalesced writer whose pending write must go out before the page dies. */
export function registerPendingWriter(flush: Flusher): () => void {
  return register(pendingWriters, flush)
}

function runAll(set: Set<Flusher>, into: Promise<unknown>[]): void {
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

/** Runs both phases; resolves when every write they issued has settled. */
export async function flushAllPersistence(): Promise<void> {
  const writes: Promise<unknown>[] = []
  runAll(positionCaptures, writes)
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
