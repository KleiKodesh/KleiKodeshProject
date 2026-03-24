import { ref } from 'vue'

declare global {
  interface Window {
    __webviewQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewPickDbPath?: () => void
    __webviewSetDbPath?: (path: string) => Promise<void>
    __webviewDbPath?: string
    __webviewDbReady?: boolean
    __onDbPathPicked?: ((path: string) => void) | null
  }
}

export const isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV
export const dbReady  = ref(isHosted ? (window.__webviewDbReady ?? import.meta.env.DEV) : true)

// Register the callback once at module load — no component lifecycle needed
if (isHosted) {
  window.__onDbPathPicked = (path: string) => {
    console.log('[db.ts] __onDbPathPicked called, path=', path)
    dbReady.value = true
  }
}

const DEV_URL = import.meta.env.VITE_DB_URL ?? 'http://localhost:4000'

export async function query<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewQuery === 'function') {
    return (await window.__webviewQuery(sql, params)).rows as T[]
  }
  const res = await fetch(`${DEV_URL}/query`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sql, params }),
  })
  if (!res.ok) throw new Error(`DB query failed: ${res.status} ${res.statusText}`)
  return (await res.json()).rows as T[]
}
