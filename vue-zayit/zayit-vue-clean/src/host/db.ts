import { ref } from 'vue'

declare global {
  interface Window {
    __webviewQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewPickDbPath?: () => void
    __webviewSetDbPath?: (path: string) => Promise<{ path: string }>
    __webviewAction?: (action: string, args?: object) => Promise<unknown>
    __webviewDbPath?: string
    __webviewDbReady?: boolean
    __onWebviewEvent?: ((msg: Record<string, unknown>) => void) | null
  }
}

export const isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV
export const dbReady  = ref(isHosted ? (window.__webviewDbReady ?? import.meta.env.DEV) : true)

console.log('[db] isHosted:', isHosted, '__webviewDbReady:', window.__webviewDbReady,
  '__webviewQuery:', typeof window.__webviewQuery,
  '__webviewAction:', typeof window.__webviewAction)

export function onDbReady(path: string) {
  window.__webviewDbPath = path
  dbReady.value = true
}

// ── Push event bus ────────────────────────────────────────────────────────────
type EventListener = (msg: Record<string, unknown>) => void
const _listeners: EventListener[] = []

export function onWebviewEvent(fn: EventListener): () => void {
  _listeners.push(fn)
  return () => { const i = _listeners.indexOf(fn); if (i !== -1) _listeners.splice(i, 1) }
}

if (isHosted) {
  window.__onWebviewEvent = (msg) => {
    console.log('[db] push event received:', msg)
    for (const fn of _listeners) fn(msg)
  }
  onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') onDbReady(msg.path as string)
  })
}

const DEV_URL = import.meta.env.VITE_DB_URL ?? 'http://localhost:4000'

export async function query<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewQuery === 'function') {
    console.log('[db] query via webview:', sql.slice(0, 60))
    try {
      const result = await window.__webviewQuery(sql, params)
      console.log('[db] query result rows:', result.rows?.length)
      return result.rows as T[]
    } catch (err) {
      console.error('[db] query error:', err)
      throw err
    }
  }
  console.log('[db] query via HTTP:', sql.slice(0, 60))
  const res = await fetch(`${DEV_URL}/query`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!res.ok) throw new Error(`DB query failed: ${res.status} ${res.statusText}`)
  return (await res.json()).rows as T[]
}
