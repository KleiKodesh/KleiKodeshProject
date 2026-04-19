import { devQueryDict } from './devFallbacks'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface DictRow {
  headword:   string
  nikud:      string | null   // from sense via sense_id
  definition: string
  source:     string | null
  source_id:  number | null
}

export interface DictSense {
  id:        number
  headword:  string
  nikud:     string | null
  pos:       string | null
  shoresh:   string | null
  binyan:    string | null
  ktiv_male: string | null
}

export interface DictRelated {
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
  SELECT e.headword, sn.nikud, e.definition, s.name AS source, e.source_id
  FROM entry e
  LEFT JOIN source s ON s.id = e.source_id
  LEFT JOIN sense sn ON sn.id = e.sense_id
  WHERE e.headword = ? LIMIT 100`

const SQL_PREFIX = `
  SELECT e.headword, sn.nikud, e.definition, s.name AS source, e.source_id
  FROM entry e
  LEFT JOIN source s ON s.id = e.source_id
  LEFT JOIN sense sn ON sn.id = e.sense_id
  WHERE e.headword LIKE ? AND e.headword != ? LIMIT 100`

const SQL_CONTAINS = `
  SELECT e.headword, sn.nikud, e.definition, s.name AS source, e.source_id
  FROM entry e
  LEFT JOIN source s ON s.id = e.source_id
  LEFT JOIN sense sn ON sn.id = e.sense_id
  WHERE e.headword LIKE ? AND e.headword NOT LIKE ? LIMIT 100`

const SQL_EXACT_IN_SENSE = `
  SELECT 1 FROM sense WHERE headword = ? LIMIT 1`

const SQL_NIKUD = `
  SELECT DISTINCT nikud FROM sense
  WHERE headword = ? AND nikud IS NOT NULL ORDER BY nikud`

const SQL_SENSE = `
  SELECT id, headword, nikud, pos, shoresh, binyan, ktiv_male
  FROM sense WHERE headword = ? LIMIT 10`

const SQL_RELATED = `
  SELECT rk.name AS kind, r.word FROM related r
  JOIN related_kind rk ON rk.id = r.kind_id
  WHERE r.sense_id IN (SELECT id FROM sense WHERE headword = ?)
    AND r.kind_id NOT IN (1, 2)
  ORDER BY r.kind_id, r.word`

const SQL_SYNONYMS = `
  SELECT DISTINCT word FROM related
  WHERE sense_id IN (SELECT id FROM sense WHERE headword = ?)
    AND kind_id = 1
  ORDER BY word`

const SQL_VARIANTS = `
  SELECT DISTINCT word FROM related
  WHERE sense_id IN (SELECT id FROM sense WHERE headword = ?)
    AND kind_id = 2
  ORDER BY word`

// ── Query ─────────────────────────────────────────────────────────────────────

async function queryDict<T>(sql: string, params: unknown[]): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  return devQueryDict<T>(sql, params)
}

// ── Exports ───────────────────────────────────────────────────────────────────

/** Returns rows for the best matching tier: exact → prefix → contains.
 *  isExact is true when the term exists in entry OR sense as an exact headword. */
export async function dictLookup(term: string): Promise<{ rows: DictRow[]; isExact: boolean }> {
  const exact = await queryDict<DictRow>(SQL_EXACT, [term])
  const inSense = exact.length === 0
    ? await queryDict<{ '1': number }>(SQL_EXACT_IN_SENSE, [term])
    : []
  const isExact = exact.length > 0 || inSense.length > 0

  if (isExact) {
    return { rows: exact, isExact: true }
  }

  const prefix = await queryDict<DictRow>(SQL_PREFIX, [`${term}%`, term])
  if (prefix.length > 0) return { rows: prefix, isExact: false }

  const contains = await queryDict<DictRow>(SQL_CONTAINS, [`%${term}%`, `${term}%`])
  return { rows: contains, isExact: false }
}

/** Distinct nikud forms for a headword. */
export async function dictNikud(term: string): Promise<string[]> {
  const rows = await queryDict<{ nikud: string }>(SQL_NIKUD, [term])
  return rows.map(r => r.nikud)
}

/** Grammar sense data (nikud, pos, שורש, בניין, כתיב מלא). */
export function dictSenses(term: string): Promise<DictSense[]> {
  return queryDict<DictSense>(SQL_SENSE, [term])
}

/** Related words (ראו גם, נגזרות, ניגודים). */
export function dictRelated(term: string): Promise<DictRelated[]> {
  return queryDict<DictRelated>(SQL_RELATED, [term])
}

/** Candidate headwords for spelling suggestions (Levenshtein). */
export async function dictSpellCandidates(term: string): Promise<string[]> {
  // Fetch candidates starting with first 2 chars to catch transpositions
  const frag2 = term.slice(0, 2)
  const frag3 = term.slice(0, 3)
  const [r2, r3] = await Promise.all([
    queryDict<{ headword: string }>(
      `SELECT DISTINCT headword FROM entry WHERE headword LIKE ? LIMIT 400`,
      [`${frag2}%`]
    ),
    frag3.length === 3
      ? queryDict<{ headword: string }>(
          `SELECT DISTINCT headword FROM sense WHERE headword LIKE ? LIMIT 200`,
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

/** Spelling variants (same word, different spelling). */
export async function dictVariants(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_VARIANTS, [term])
  return rows.map(r => r.word)
}

/** Synonym/translation pairs. */
export async function dictSynonyms(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_SYNONYMS, [term])
  return rows.map(r => r.word)
}
