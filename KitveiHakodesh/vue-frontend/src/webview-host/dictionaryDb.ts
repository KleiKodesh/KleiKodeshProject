// Dictionary DB query layer for KitveiHakodesh_dictionary.db.
// Hosted (C#) still runs the SQL from dictionaryDb.sql.ts via the dict-sql bridge.
// Dev routes through the KitveiHakodesh service (serviceCall), which owns the SQL —
// the browser dev build no longer sends dictionary SQL.
// Seforim DB queries (מצודת ציון, מלבי"ם, מחברת מנחם) live in dictionarySeforimDb.ts.

import { serviceCall } from './serviceClient'
import {
  boldExact, boldPrefix, boldContains,
  getMetzudatBookIds, getMalbimBookIds,
  menchemLookup, aruchLookup, micropediaLookup,
} from './dictionarySeforimDb'
import {
  SQL_DICT_EXACT, SQL_DICT_PREFIX, SQL_DICT_CONTAINS, SQL_DICT_EXACT_IN_WORD,
  SQL_DICT_ABBREV_CONTAINS,
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

export type { MetzudatRow, MenchemRow, AruchRow, MicropediaRow } from './dictionarySeforimDb'

declare global {
  interface Window {
    __webviewDictQuery?: (sql: string, params: unknown[]) => Promise<{ rows: unknown[] }>
  }
}

// ── Query transport ───────────────────────────────────────────────────────────

/** True when the C# dict-sql bridge is present (hosted). Dev falls to the service. */
const isDictHosted = (): boolean => typeof window.__webviewDictQuery === 'function'

/** Hosted-only transport — runs SQL via the C# dict-sql bridge. */
async function queryDictHosted<T>(sql: string, params: unknown[]): Promise<T[]> {
  return (await window.__webviewDictQuery!(sql, params)).rows as T[]
}

// ── Dictionary tier queries ───────────────────────────────────────────────────

async function dictExact(term: string): Promise<{ rows: SenseRow[]; isExact: boolean }> {
  if (!isDictHosted())
    return serviceCall<{ rows: SenseRow[]; isExact: boolean }>('dictExact', { term })
  const rows = await queryDictHosted<SenseRow>(SQL_DICT_EXACT, [term])
  if (rows.length > 0) return { rows, isExact: true }
  const hit = await queryDictHosted<{ '1': number }>(SQL_DICT_EXACT_IN_WORD, [term])
  return { rows: [], isExact: hit.length > 0 }
}

async function dictPrefix(term: string): Promise<SenseRow[]> {
  if (!isDictHosted()) return (await serviceCall<{ rows: SenseRow[] }>('dictPrefix', { term })).rows
  return queryDictHosted<SenseRow>(SQL_DICT_PREFIX, [`${term}%`, term])
}

async function dictContains(term: string): Promise<SenseRow[]> {
  if (!isDictHosted()) return (await serviceCall<{ rows: SenseRow[] }>('dictContains', { term })).rows
  return queryDictHosted<SenseRow>(SQL_DICT_CONTAINS, [`%${term}%`, `${term}%`])
}

// ── Exported dictionary functions ─────────────────────────────────────────────

/** Related words (ראו גם, נגזרות, ניגודים — excludes כתיב variants). */
export async function dictLinks(term: string): Promise<DictLink[]> {
  if (!isDictHosted()) return (await serviceCall<{ links: DictLink[] }>('dictLinks', { term })).links
  return queryDictHosted<DictLink>(SQL_DICT_LINKS, [term])
}

/** Synonym words (נרדף). */
export async function dictSynonyms(term: string): Promise<string[]> {
  if (!isDictHosted()) return (await serviceCall<{ words: string[] }>('dictSynonyms', { term })).words
  const rows = await queryDictHosted<{ word: string }>(SQL_DICT_SYNONYMS, [term])
  return rows.map(r => r.word)
}

/** Spelling variants — same word different spelling (כתיב). */
export async function dictVariants(term: string): Promise<string[]> {
  if (!isDictHosted()) return (await serviceCall<{ words: string[] }>('dictVariants', { term })).words
  const rows = await queryDictHosted<{ word: string }>(SQL_DICT_VARIANTS, [term])
  return rows.map(r => r.word)
}

/** Candidate headwords for spelling suggestions (Levenshtein). */
export async function dictSpellCandidates(term: string): Promise<string[]> {
  if (!isDictHosted())
    return (await serviceCall<{ words: string[] }>('dictSpellCandidates', { term })).words
  const frag2 = term.slice(0, 2)
  const frag3 = term.slice(0, 3)
  const [r2, r3] = await Promise.all([
    queryDictHosted<{ headword: string }>(SQL_DICT_SPELL_CANDIDATES_FRAG2, [`${frag2}%`]),
    frag3.length === 3
      ? queryDictHosted<{ headword: string }>(SQL_DICT_SPELL_CANDIDATES_FRAG3, [`${frag3}%`])
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
 * Dictionary-only abbreviation lookup for the book-view selection tooltip.
 * Candidates are tried in order (full term first, then prefix-stripped forms
 * like מהשי"ת → השי"ת): all exact matches first, then %candidate% LIKE
 * fallbacks. Returns the first candidate that matched together with its rows.
 * Never touches the seforim DB (unlike abbrevLookup/combinedLookup).
 */
export async function dictAbbrevSenses(
  candidates: string[],
): Promise<{ matched: string; rows: SenseRow[] } | null> {
  if (!isDictHosted()) {
    const r = await serviceCall<{ matched: string | null; rows: SenseRow[] }>('dictAbbrevSenses', { candidates })
    return r.matched ? { matched: r.matched, rows: r.rows } : null
  }
  for (const candidate of candidates) {
    const rows = await queryDictHosted<SenseRow>(SQL_DICT_EXACT, [candidate])
    if (rows.length > 0) return { matched: candidate, rows }
  }
  for (const candidate of candidates) {
    const rows = await queryDictHosted<SenseRow>(SQL_DICT_ABBREV_CONTAINS, [`%${candidate}%`])
    if (rows.length > 0) return { matched: candidate, rows }
  }
  return null
}

/**
 * Given a list of candidate headwords (כתיב מלא expansions), returns only those
 * that actually exist in the word table. Single IN query — no sense data fetched.
 */
export async function dictKetivVariants(candidates: string[]): Promise<string[]> {
  if (candidates.length === 0) return []
  if (!isDictHosted())
    return (await serviceCall<{ words: string[] }>('dictKetivVariants', { candidates })).words
  const rows = await queryDictHosted<{ headword: string }>(
    buildKetivExistsQuery(candidates.length),
    candidates,
  )
  return rows.map(r => r.headword)
}

// ── Combined lookup ───────────────────────────────────────────────────────────

export interface CombinedLookupResult {
  dictRows:        SenseRow[]
  metzudatRows:    import('./dictionarySeforimDb').MetzudatRow[]
  malbimRows:      import('./dictionarySeforimDb').MetzudatRow[]
  menchemRows:     import('./dictionarySeforimDb').MenchemRow[]
  micropediaRows:  import('./dictionarySeforimDb').MicropediaRow[]
  aruchRows:       import('./dictionarySeforimDb').AruchRow[]
  isExact:         boolean
}

/**
 * Runs dictionary + מצודת ציון + מלבי"ם in parallel through a shared tier
 * progression: exact → prefix → contains. All three exact queries fire together;
 * if any source finds results the tier is done and lower tiers are skipped.
 *
 * מחברת מנחם, מיקרופדיה and ספר הערוך run independently in parallel (exact-only,
 * different structure) and do not participate in the tier gating.
 */
export async function combinedLookup(term: string): Promise<CombinedLookupResult> {
  const [metzudatIds, malbimIds] = await Promise.all([
    getMetzudatBookIds(),
    getMalbimBookIds(),
  ])

  // מחברת מנחם, מיקרופדיה and ספר הערוך are exact-only — fire them immediately and collect at the end
  const menchemPromise = menchemLookup(term)
  const micropediaPromise = micropediaLookup(term)
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
      menchemRows:    await menchemPromise,
      micropediaRows: await micropediaPromise,
      aruchRows:      await aruchPromise,
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
      menchemRows:    await menchemPromise,
      micropediaRows: await micropediaPromise,
      aruchRows:      await aruchPromise,
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
    menchemRows:    await menchemPromise,
    micropediaRows: await micropediaPromise,
    aruchRows:      await aruchPromise,
    isExact:        false,
  }
}
