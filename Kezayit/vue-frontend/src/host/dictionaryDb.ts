import { devQueryDict } from './devFallbacks'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface SenseRow {
  headword:   string
  nikud:      string | null
  text:       string
  source:     string | null
  source_id:  number | null
}

export interface DictLink {
  kind: string
  word: string
}

declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

// ── SQL ───────────────────────────────────────────────────────────────────────

const SQL_EXACT = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword = ? LIMIT 100`

const SQL_PREFIX = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword LIKE ? AND w.headword != ? LIMIT 100`

const SQL_CONTAINS = `
  SELECT w.headword, s.nikud, s.text, sk.name AS source, s.source_id
  FROM word w
  JOIN sense s ON s.word_id = w.id
  LEFT JOIN source_kind sk ON sk.id = s.source_id
  WHERE w.headword LIKE ? AND w.headword NOT LIKE ? LIMIT 100`

const SQL_EXACT_IN_WORD = `
  SELECT 1 FROM word WHERE headword = ? LIMIT 1`

// kind values: נרדף | כתיב | ראו_גם | ניגוד | נגזרת
const SQL_LINKS = `
  SELECT lk.name AS kind, w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ?
    AND lk.name != 'כתיב'
  ORDER BY lk.name, w2.headword`

const SQL_SYNONYMS = `
  SELECT w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ? AND lk.name = 'נרדף'
  ORDER BY w2.headword`

const SQL_VARIANTS = `
  SELECT w2.headword AS word
  FROM link l
  JOIN word w1 ON w1.id = l.word_id
  JOIN word w2 ON w2.id = l.target_id
  JOIN link_kind lk ON lk.id = l.kind_id
  WHERE w1.headword = ? AND lk.name = 'כתיב'
  ORDER BY w2.headword`

// ── Query ─────────────────────────────────────────────────────────────────────

async function queryDict<T>(sql: string, params: unknown[]): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  return devQueryDict<T>(sql, params)
}

// ── Exports ───────────────────────────────────────────────────────────────────

/** Returns rows for the best matching tier: exact → prefix → contains.
 *  isExact is true when the term exists as an exact headword. */
export async function dictLookup(term: string): Promise<{ rows: SenseRow[]; isExact: boolean }> {
  const exact = await queryDict<SenseRow>(SQL_EXACT, [term])
  const inWord = exact.length === 0
    ? await queryDict<{ '1': number }>(SQL_EXACT_IN_WORD, [term])
    : []
  const isExact = exact.length > 0 || inWord.length > 0

  if (isExact) return { rows: exact, isExact: true }

  const prefix = await queryDict<SenseRow>(SQL_PREFIX, [`${term}%`, term])
  if (prefix.length > 0) return { rows: prefix, isExact: false }

  const contains = await queryDict<SenseRow>(SQL_CONTAINS, [`%${term}%`, `${term}%`])
  return { rows: contains, isExact: false }
}

/** Related words (ראו גם, נגזרות, ניגודים — excludes כתיב variants). */
export function dictLinks(term: string): Promise<DictLink[]> {
  return queryDict<DictLink>(SQL_LINKS, [term])
}

/** Synonym words (נרדף). */
export async function dictSynonyms(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_SYNONYMS, [term])
  return rows.map(r => r.word)
}

/** Spelling variants — same word different spelling (כתיב). */
export async function dictVariants(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_VARIANTS, [term])
  return rows.map(r => r.word)
}

/** Candidate headwords for spelling suggestions (Levenshtein). */
export async function dictSpellCandidates(term: string): Promise<string[]> {
  const frag2 = term.slice(0, 2)
  const frag3 = term.slice(0, 3)
  const [r2, r3] = await Promise.all([
    queryDict<{ headword: string }>(
      `SELECT headword FROM word WHERE headword LIKE ? LIMIT 400`,
      [`${frag2}%`]
    ),
    frag3.length === 3
      ? queryDict<{ headword: string }>(
          `SELECT headword FROM word WHERE headword LIKE ? LIMIT 200`,
          [`${frag3}%`]
        )
      : Promise.resolve([]),
  ])
  const seen = new Set<string>()
  const out: string[] = []
  for (const r of [...r2, ...r3]) {
    if (!seen.has(r.headword)) { seen.add(r.headword); out.push(r.headword) }
  }
  return out
}

/** Abbreviation lookup — delegates to dictLookup since abbreviations
 *  are stored in the same sense table as regular words. */
export async function abbrevLookup(term: string): Promise<SenseRow[]> {
  const { rows } = await dictLookup(term)
  return rows
}
