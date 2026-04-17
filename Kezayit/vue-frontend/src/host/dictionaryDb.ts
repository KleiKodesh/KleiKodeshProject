import { devQueryDict } from './devFallbacks'

declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

/** Queries the Kezayit dictionary database (kezayit_dictionary.db). */
export async function queryDict<T = unknown>(sql: string, params: unknown[] = []): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  return devQueryDict<T>(sql, params)
}
