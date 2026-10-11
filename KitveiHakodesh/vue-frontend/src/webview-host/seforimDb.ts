import { ref } from 'vue'

/**
 * True when the C# host's bridge channel is present — i.e. we are running inside WebView2 and
 * KitveiHakodeshLib is available to serve data and drive native features.
 *
 * THIS is the real hosted/dev distinction, and the only one worth branching on. It replaced an
 * `isHosted` that could never be false (`__webviewDbReady !== undefined || import.meta.env.DEV`
 * — DEV in dev, and the host always injects __webviewDbReady, true OR false, in hosted), so
 * every `if (!isHosted)` guard was dead code and every `isHosted ? hosted : dev` branch silently
 * took the hosted path in dev. That produced real bugs three times over (paste into Word,
 * export to Word, font detection).
 *
 * Both runtimes read their data from the KitveiHakodesh service (serviceClient.ts); what
 * this flag selects is who hosts the page: WebView2 with its native features, or a browser.
 */
export const hasHostBridge = typeof window.__webviewAction === 'function'

/**
 * Whether the seforim DB is available. Optimistically true at startup so first paint is
 * not held back by a round-trip; `probeDbReady` (called from main.ts) asks the service
 * whether the resolved file actually exists on disk and corrects it.
 */
export const dbReady = ref(true)

/** Ask the service whether the resolved seforim DB exists; updates `dbReady`. */
export async function probeDbReady(): Promise<boolean> {
  try {
    const { serviceCall } = await import('./serviceClient')
    const info = await serviceCall<{ path: string; exists: boolean }>('getSeforimDbPath')
    dbReady.value = !!info?.exists
  } catch {
    /* service unreachable — leave the optimistic value; the data calls will report it */
  }
  return dbReady.value
}

/** True once detected; false means the column doesn't exist or detection hasn't run yet. */
export let categoryHasOrderIndex = false

/**
 * Column that flags a link as a DECLARED base-text relationship. The two seforim-DB
 * schemas spell the same idea differently: Zayit's own DB has `isDeclaredBase` (0/1),
 * the newer otzaria build has `baseProvenance` (0=none, 1=inferred, 2=declared). Both
 * mean "> 0 = the data asserts this", so only the name varies. Zayit's is the safe
 * default because that schema is the baseline; the probe upgrades it when present.
 */
export let declaredBaseColumn = 'isDeclaredBase'

/**
 * True when line text lives in the `line_content` sidecar and `line` itself has no
 * `content` column - SeforimLibrary schema 6 and later.
 *
 * This decides the SHAPE of every query that reads line text, not merely how a value is
 * read: on such a database `SELECT content FROM line` fails outright with "no such
 * column". The queries in queries.sql.ts take this flag and join the sidecar instead.
 *
 * False by default, which is the older layout, and the probe below upgrades it.
 */
export let hasSplitLineContent = false

let _schemaDetected = false
let _schemaDetecting: Promise<void> | null = null

/**
 * Kept as a no-op so callers need not care which runtime they are in.
 *
 * It used to run three PRAGMA table_info round-trips at startup to decide which spelling of
 * the hosted SQL to use — whether `category` has orderIndex, which column flags a declared
 * base text, whether line content moved to a sidecar. C# answers all three from its own
 * schema probe now, on both legs, so the page has nothing to detect.
 */
export function ensureCategorySchema(): Promise<void> {
  return Promise.resolve()
}

/**
 * After the service accepted a new seforim DB path (pick, typed, or reset to default).
 *
 * A DIFFERENT library means every stored book id now points somewhere else, so the recent
 * books and the rest of the book-id-keyed state are dropped before the reload — see
 * webview-host/dbSwitchCleanup. Compared against the path being left, so first-ever setup
 * (no previous path) and re-picking the same folder both keep the reader's history.
 *
 * The service restarts itself on the new DB; the reload makes every store refetch from
 * it, and the service client rides out the restart.
 */
export async function onDbPathChanged(previousPath: string, newPath: string) {
  dbReady.value = true
  // Awaited, not fire-and-forget: reloading mid-wipe would leave stale data behind.
  if (previousPath && previousPath !== newPath) {
    const { clearStaleBookData } = await import('./dbSwitchCleanup')
    await clearStaleBookData()
  }
  setTimeout(() => window.location.reload(), 800)
}

// ── Push event bus ────────────────────────────────────────────────────────────
type EventListener = (msg: Record<string, unknown>) => void
const _listeners: EventListener[] = []

export function onWebviewEvent(fn: EventListener): () => void {
  _listeners.push(fn)
  return () => {
    const i = _listeners.indexOf(fn)
    if (i !== -1) _listeners.splice(i, 1)
  }
}

/**
 * Dispatch a push event to all listeners locally. In hosted mode C# owns this channel
 * (window.__onWebviewEvent); in DEV there is no C# push side, so bridge functions that
 * replicate a hosted push-based flow (e.g. the HebrewBooks download, which the hosted app
 * finishes with an hbPdfReady/hbPdfCancelled push) call this after their service round-trip
 * so the existing store listeners fire unchanged. No-op semantics are identical to a C# push.
 */
export function emitWebviewEvent(msg: Record<string, unknown>): void {
  for (const fn of _listeners) fn(msg)
}

// Only the C# host pushes events into this channel; in dev the bridge functions call
// emitWebviewEvent directly after their service round-trip.
if (hasHostBridge) {
  window.__onWebviewEvent = (msg) => {
    for (const fn of _listeners) fn(msg)
  }
}
