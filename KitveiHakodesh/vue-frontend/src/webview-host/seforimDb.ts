import { ref } from 'vue'
import { devQuery } from './devFallbacks'

declare global {
  interface Window {
    __webviewQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewPickDbPath?: () => void
    __webviewSetDbPath?: (path: string) => Promise<{ path: string }>
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
    __webviewDbPath?: string
    __webviewDbReady?: boolean
    __webviewShowPopOut?: boolean
    __onWebviewEvent?: ((msg: Record<string, unknown>) => void) | null
  }
}

export const isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV
export const dbReady = ref(isHosted ? (window.__webviewDbReady ?? import.meta.env.DEV) : true)

/**
 * True when C# sets ShowPopOutButton = true on AppViewer (VSTO task-pane context).
 * Controls visibility of the "חלון עצמאי / חלונית" button in the hamburger menu.
 * Defaults to false in all other environments (standalone demo, browser dev).
 */
export const showPopOutButton = window.__webviewShowPopOut === true

/** True once detected; false means the column doesn't exist or detection hasn't run yet. */
export let categoryHasOrderIndex = false

let _schemaDetected = false
let _schemaDetecting: Promise<void> | null = null

/** Lazy — only runs on first call. Safe to call multiple times. */
export function ensureCategorySchema(): Promise<void> {
  if (_schemaDetected) return Promise.resolve()
  if (_schemaDetecting) return _schemaDetecting
  _schemaDetecting = query<{ name: string }>('PRAGMA table_info(category)', [])
    .then((cols) => {
      categoryHasOrderIndex = cols.some((c) => c.name === 'orderIndex')
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

export function onDbReady(path: string) {
  window.__webviewDbPath = path
  dbReady.value = true
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

if (isHosted) {
  window.__onWebviewEvent = (msg) => {
    for (const fn of _listeners) fn(msg)
  }
  onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') {
      onDbReady(msg.path as string)
    }
  })
}

export async function query<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewQuery === 'function') {
    return (await window.__webviewQuery(sql, params)).rows as T[]
  }
  // In the hosted environment without a DB (user skipped setup), return empty
  // results rather than falling through to the dev fetch which would fail.
  if (isHosted && !dbReady.value) return []
  return devQuery<T>(sql, params)
}
