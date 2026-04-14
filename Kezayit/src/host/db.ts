import { ref } from 'vue'

declare global {
  interface Window {
    __webviewQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewWikiDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewPickDbPath?: () => void
    __webviewSetDbPath?: (path: string) => Promise<{ path: string }>
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
    __webviewDbPath?: string
    __webviewDbReady?: boolean
    __onWebviewEvent?: ((msg: Record<string, unknown>) => void) | null
  }
}

export const isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV
export const dbReady = ref(isHosted ? (window.__webviewDbReady ?? import.meta.env.DEV) : true)

/** True once detected; false means the column doesn't exist or detection hasn't run yet. */
export let categoryHasOrderIndex = false

let _schemaDetected = false
let _schemaDetecting: Promise<void> | null = null

/** Lazy — only runs on first call. Safe to call multiple times. */
export function ensureCategorySchema(): Promise<void> {
  if (_schemaDetected) return Promise.resolve()
  if (_schemaDetecting) return _schemaDetecting
  _schemaDetecting = query<{ name: string }>('PRAGMA table_info(category)', []).then((cols) => {
    categoryHasOrderIndex = cols.some((c) => c.name === 'orderIndex')
    _schemaDetected = true
    _schemaDetecting = null
  })
  return _schemaDetecting
}

export function onDbReady(path: string) {
  window.__webviewDbPath = path
  dbReady.value = true
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
  // Dev fallback — hits the Vite middleware at /query (same origin, no separate server needed)
  const res = await fetch('/query', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!res.ok) throw new Error(`DB query failed: ${res.status} ${res.statusText}`)
  return (await res.json()).rows as T[]
}

export async function queryDict<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  const res = await fetch('/query-dict', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  const json = await res.json()
  if (!res.ok) throw new Error(`Dict query failed: ${json.error ?? res.statusText}`)
  return json.rows as T[]
}

export async function queryWikiDict<T = unknown>(
  sql: string,
  params: unknown[] = [],
): Promise<T[]> {
  if (typeof window.__webviewWikiDictQuery === 'function') {
    return (await window.__webviewWikiDictQuery(sql, params)).rows as T[]
  }
  const res = await fetch('/query-wikidict', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  const json = await res.json()
  if (!res.ok) throw new Error(`WikiDict query failed: ${json.error ?? res.statusText}`)
  return json.rows as T[]
}
