import { devQueryDict } from './devFallbacks'

export interface DictRow {
  headword:   string
  nikud:      string | null
  definition: string
  source:     string | null
}

declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

const SQL_LOOKUP = `
  SELECT e.headword, e.nikud, e.definition, s.name AS source
  FROM entry e
  LEFT JOIN source s ON s.id = e.source_id
  WHERE e.headword = ? OR e.headword LIKE ?
  ORDER BY CASE WHEN e.headword = ? THEN 0 ELSE 1 END, length(e.headword), e.headword
  LIMIT 100`

const SQL_FUZZY = `
  SELECT DISTINCT headword
  FROM entry
  WHERE headword LIKE ?
  LIMIT 200`

async function queryDict<T>(sql: string, params: unknown[]): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  return devQueryDict<T>(sql, params)
}

/** Full dictionary lookup — forward and reverse via headword index. */
export function dictLookup(term: string): Promise<DictRow[]> {
  return queryDict<DictRow>(SQL_LOOKUP, [term, `${term}%`, term])
}

/** Fuzzy candidate headwords for Levenshtein fallback. */
export async function dictFuzzyCandidates(containsPattern: string): Promise<string[]> {
  const rows = await queryDict<{ headword: string }>(SQL_FUZZY, [containsPattern])
  return rows.map((r) => r.headword)
}
