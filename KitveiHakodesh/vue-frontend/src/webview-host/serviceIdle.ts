import { allHostWindows } from '@/composables/detachedWindow/hostWindow'

/**
 * Counts service calls in flight so startup can tell when the restored view has its data.
 *
 * The desktop host shows a snapshot of the last session until the page says its content
 * is drawn ('contentReady'). 'appReady' is too early for that: Vue has mounted, but the
 * restored tab is still fetching its lines, so hiding the snapshot then shows an empty
 * page that refills — it looks like a reload.
 */

let inFlight = 0
let idleWaiters: Array<() => void> = []

export function serviceCallStarted(): void {
  inFlight++
}

export function serviceCallEnded(): void {
  inFlight = Math.max(0, inFlight - 1)
  if (inFlight === 0) {
    const waiters = idleWaiters
    idleWaiters = []
    for (const resolve of waiters) resolve()
  }
}

/** Resolves the next time no service call is in flight (immediately if none is). */
function whenServiceIdle(): Promise<void> {
  if (inFlight === 0) return Promise.resolve()
  return new Promise((resolve) => idleWaiters.push(resolve))
}


// A frame of a window on screen. The desktop app's own window only hosts the page and stays
// hidden (window.__khsPageHost), and a hidden window draws no frames: there, a frame of one of
// the windows the page opened. With none open yet, the next task stands in.
const nextFrame = () =>
  new Promise<void>((resolve) => {
    const shown = window.__khsPageHost ? allHostWindows().find((w) => w !== window) : window
    if (shown) shown.requestAnimationFrame(() => resolve())
    else setTimeout(resolve, 0)
  })

/**
 * Resolves once the startup view has fetched and painted: one frame for the restored
 * views to issue their fetches, then — repeated while new calls keep starting — the
 * service going idle followed by two frames for the fetched content to render. The
 * repeat covers chained loads (book meta, then its lines), where the count touches
 * zero between the two calls.
 */
export async function whenStartupContentDrawn(): Promise<void> {
  await nextFrame()
  do {
    await whenServiceIdle()
    await nextFrame()
    await nextFrame()
  } while (inFlight > 0)
}
