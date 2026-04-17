declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
    __webviewWikiDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

/** Queries the Aramaic dictionary database (kezayit_dictionary.db). */
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

/** Queries the Hebrew Wiktionary database (wikidictionary.db). */
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
