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
 * Remember the architecture it selects between: hosted does NOT use the KitveiHakodesh service —
 * KitveiHakodeshLib owns the data and the native calls there. Only dev goes through the service.
 */
export const hasHostBridge = typeof window.__webviewAction === 'function'

/**
 * Whether the seforim DB is available.
 *
 * Hosted: the C# host injects __webviewDbReady (false when the user skipped DB setup).
 * Dev: always true — the service resolves and owns the DB; SetupWizard asks it separately
 * (getDbPathInfo) whether the file actually exists on disk.
 */
export const dbReady = ref(window.__webviewDbReady ?? true)

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

let _schemaDetected = false
let _schemaDetecting: Promise<void> | null = null

/** Lazy — only runs on first call. Safe to call multiple times. */
export function ensureCategorySchema(): Promise<void> {
  if (_schemaDetected) return Promise.resolve()
  // Dev routes the catalog through the service, which detects the orderIndex column
  // itself — categoryHasOrderIndex is only consulted on the hosted (C#) SQL path.
  if (typeof window.__webviewQuery !== 'function') {
    _schemaDetected = true
    return Promise.resolve()
  }
  if (_schemaDetecting) return _schemaDetecting
  _schemaDetecting = Promise.all([
    query<{ name: string }>('PRAGMA table_info(category)', []),
    query<{ name: string }>('PRAGMA table_info(link)', []),
  ])
    .then(([categoryCols, linkCols]) => {
      categoryHasOrderIndex = categoryCols.some((c) => c.name === 'orderIndex')
      if (linkCols.some((c) => c.name === 'baseProvenance')) declaredBaseColumn = 'baseProvenance'
      _schemaDetected = true
    })
    .catch(() => {
      // Schema detection failed — proceed with the safe default (no orderIndex)
      categoryHasOrderIndex = false
      _schemaDetected = true
    })
    .finally(() => {
      _schemaDetecting = null
    })
  return _schemaDetecting
}

export async function onDbReady(path: string) {
  // A DIFFERENT library means every stored book id now points somewhere else, so the
  // recent books and the rest of the book-id-keyed state are dropped before the
  // reload — see webview-host/dbSwitchCleanup. Compared against the path we are
  // leaving, so first-ever setup (no previous path) and re-picking the same folder
  // both keep the reader's history.
  const previousPath = window.__webviewDbPath
  const isSwitch = !!previousPath && previousPath !== path
  window.__webviewDbPath = path
  dbReady.value = true
  // Awaited, not fire-and-forget: the reload below is what makes the surviving stores
  // refetch, and reloading mid-wipe would leave some of the stale data behind.
  if (isSwitch) {
    const { clearStaleBookData } = await import('./dbSwitchCleanup')
    await clearStaleBookData()
  }
  // Ask C# to reload via its HandleReload() method, which re-reads the saved path from
  // the registry, updates the __webviewDbReady injection script, then navigates.
  // window.location.reload() bypasses that and would re-inject the old "false" value.
  if (typeof window.__webviewAction === 'function') {
    window.__webviewAction('reload').catch(() => window.location.reload())
  } else {
    window.location.reload()
  }
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
  onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') {
      // A push has nobody to return the promise to; onDbReady contains its own
      // failures, so this only keeps an unexpected rejection out of the console.
      void onDbReady(msg.path as string).catch(() => {})
    }
    // C# pushes the FULL exception (type + stack) of any failure in the DB path
    // flow here. Nothing used to listen, so a failed pick was invisible — log it
    // so an affected user's F12 console names the exact failing step.
    if (msg.event === 'dbOpenError') {
      console.error('[seforimDb] DB path change failed (dbOpenError):\n' + String(msg.error ?? ''))
    }
  })
}

export async function query<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewQuery === 'function') {
    return (await window.__webviewQuery(sql, params)).rows as T[]
  }
  // In the hosted environment without a DB (user skipped setup), return empty results.
  if (hasHostBridge && !dbReady.value) return []
  // Dev seforim access goes through seforimApi (the KitveiHakodesh service), not here.
  throw new Error('seforimDb.query() is hosted-only; dev uses seforimApi')
}
