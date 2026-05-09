// Dictionary DB query layer for KitveiHakodesh_dictionary.db.
// All SQL strings live in dictionaryDb.sql.ts.
// Seforim DB queries (מצודת ציון, מלבי"ם, מחברת מנחם) live in dictionarySeforimDb.ts.

import { devQueryDict } from './devFallbacks'
import {
  boldExact, boldPrefix, boldContains,
  getMetzudatBookIds, getMalbimBookIds,
  menchemLookup, aruchLookup,
} from './dictionarySeforimDb'
import {
  SQL_DICT_EXACT, SQL_DICT_PREFIX, SQL_DICT_CONTAINS, SQL_DICT_EXACT_IN_WORD,
  SQL_DICT_LINKS, SQL_DICT_SYNONYMS, SQL_DICT_VARIANTS,
  SQL_DICT_SPELL_CANDIDATES_FRAG2, SQL_DICT_SPELL_CANDIDATES_FRAG3,
  buildKetivExistsQuery,
} from './dictionaryDb.sql'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface SenseRow {
  headword:  string
  nikud:     string | null
  text:      string
  source:    string | null
  source_id: number | null
}

export interface DictLink {
  kind: string
  word: string
}

export type { MetzudatRow, MenchemRow, AruchRow } from './dictionarySeforimDb'

declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

// ── Query transport ───────────────────────────────────────────────────────────

async function queryDict<T>(sql: string, params: unknown[]): Promise<T[]> {
  if (typeof window.__webviewDictQuery === 'function') {
    return (await window.__webviewDictQuery(sql, params)).rows as T[]
  }
  return devQueryDict<T>(sql, params)
}

// ── Dictionary tier queries ───────────────────────────────────────────────────

async function dictExact(term: string): Promise<{ rows: SenseRow[]; isExact: boolean }> {
  const rows = await queryDict<SenseRow>(SQL_DICT_EXACT, [term])
  if (rows.length > 0) return { rows, isExact: true }
  const hit = await queryDict<{ '1': number }>(SQL_DICT_EXACT_IN_WORD, [term])
  return { rows: [], isExact: hit.length > 0 }
}

async function dictPrefix(term: string): Promise<SenseRow[]> {
  return queryDict<SenseRow>(SQL_DICT_PREFIX, [`${term}%`, term])
}

async function dictContains(term: string): Promise<SenseRow[]> {
  return queryDict<SenseRow>(SQL_DICT_CONTAINS, [`%${term}%`, `${term}%`])
}

// ── Exported dictionary functions ─────────────────────────────────────────────

/** Related words (ראו גם, נגזרות, ניגודים — excludes כתיב variants). */
export function dictLinks(term: string): Promise<DictLink[]> {
  return queryDict<DictLink>(SQL_DICT_LINKS, [term])
}

/** Synonym words (נרדף). */
export async function dictSynonyms(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_DICT_SYNONYMS, [term])
  return rows.map(r => r.word)
}

/** Spelling variants — same word different spelling (כתיב). */
export async function dictVariants(term: string): Promise<string[]> {
  const rows = await queryDict<{ word: string }>(SQL_DICT_VARIANTS, [term])
  return rows.map(r => r.word)
}

/** Candidate headwords for spelling suggestions (Levenshtein). */
export async function dictSpellCandidates(term: string): Promise<string[]> {
  const frag2 = term.slice(0, 2)
  const frag3 = term.slice(0, 3)
  const [r2, r3] = await Promise.all([
    queryDict<{ headword: string }>(SQL_DICT_SPELL_CANDIDATES_FRAG2, [`${frag2}%`]),
    frag3.length === 3
      ? queryDict<{ headword: string }>(SQL_DICT_SPELL_CANDIDATES_FRAG3, [`${frag3}%`])
      : Promise.resolve([]),
  ])
  const seen = new Set<string>()
  const out: string[] = []
  for (const r of [...r2, ...r3]) {
    if (!seen.has(r.headword)) { seen.add(r.headword); out.push(r.headword) }
  }
  return out
}

/** Abbreviation lookup — delegates to combinedLookup (abbreviations are in the sense table). */
export async function abbrevLookup(term: string): Promise<SenseRow[]> {
  const { dictRows } = await combinedLookup(term)
  return dictRows
}

/**
 * Given a list of candidate headwords (כתיב מלא expansions), returns only those
 * that actually exist in the word table. Single IN query — no sense data fetched.
 */
export async function dictKetivVariants(candidates: string[]): Promise<string[]> {
  if (candidates.length === 0) return []
  const rows = await queryDict<{ headword: string }>(
    buildKetivExistsQuery(candidates.length),
    candidates,
  )
  return rows.map(r => r.headword)
}

// ── Combined lookup ───────────────────────────────────────────────────────────

export interface CombinedLookupResult {
  dictRows:     SenseRow[]
  metzudatRows: import('./dictionarySeforimDb').MetzudatRow[]
  malbimRows:   import('./dictionarySeforimDb').MetzudatRow[]
  menchemRows:  import('./dictionarySeforimDb').MenchemRow[]
  aruchRows:    import('./dictionarySeforimDb').AruchRow[]
  isExact:      boolean
}

/**
 * Runs dictionary + מצודת ציון + מלבי"ם in parallel through a shared tier
 * progression: exact → prefix → contains. All three exact queries fire together;
 * if any source finds results the tier is done and lower tiers are skipped.
 *
 * מחברת מנחם and ספר הערוך run independently in parallel (exact-only, different structure)
 * and do not participate in the tier gating.
 */
export async function combinedLookup(term: string): Promise<CombinedLookupResult> {
  const [metzudatIds, malbimIds] = await Promise.all([
    getMetzudatBookIds(),
    getMalbimBookIds(),
  ])

  // מחברת מנחם and ספר הערוך are exact-only — fire them immediately and collect at the end
  const menchemPromise = menchemLookup(term)
  const aruchPromise = aruchLookup(term)

  // Tier 1 — exact
  const [dictExactResult, metzudatExactRows, malbimExactRows] = await Promise.all([
    dictExact(term),
    boldExact(term, metzudatIds),
    boldExact(term, malbimIds),
  ])

  if (dictExactResult.isExact || metzudatExactRows.length > 0 || malbimExactRows.length > 0) {
    return {
      dictRows:     dictExactResult.rows,
      metzudatRows: metzudatExactRows,
      malbimRows:   malbimExactRows,
      menchemRows:  await menchemPromise,
      aruchRows:    await aruchPromise,
      isExact:      true,
    }
  }

  // Tier 2 — prefix
  const [dictPrefixRows, metzudatPrefixRows, malbimPrefixRows] = await Promise.all([
    dictPrefix(term),
    boldPrefix(term, metzudatIds),
    boldPrefix(term, malbimIds),
  ])

  if (dictPrefixRows.length > 0 || metzudatPrefixRows.length > 0 || malbimPrefixRows.length > 0) {
    return {
      dictRows:     dictPrefixRows,
      metzudatRows: metzudatPrefixRows,
      malbimRows:   malbimPrefixRows,
      menchemRows:  await menchemPromise,
      aruchRows:    await aruchPromise,
      isExact:      false,
    }
  }

  // Tier 3 — contains
  const [dictContainsRows, metzudatContainsRows, malbimContainsRows] = await Promise.all([
    dictContains(term),
    boldContains(term, metzudatIds),
    boldContains(term, malbimIds),
  ])

  return {
    dictRows:     dictContainsRows,
    metzudatRows: metzudatContainsRows,
    malbimRows:   malbimContainsRows,
    menchemRows:  await menchemPromise,
    aruchRows:    await aruchPromise,
    isExact:      false,
  }
}
