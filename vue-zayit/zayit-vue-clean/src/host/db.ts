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
export const dbReady = ref(isHosted ? (window.__webviewDbReady ?? import.meta.env.DEV) : true)

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
    console.log('[db.ts] webview event:', msg.event, msg)
    for (const fn of _listeners) fn(msg)
  }
  onWebviewEvent((msg) => {
    if (msg.event === 'dbPathPicked') {
      console.log('[db.ts] dbPathPicked, path=', msg.path)
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
