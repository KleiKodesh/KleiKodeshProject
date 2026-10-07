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

/** True when the C# bridge is present (hosted). Dev falls to the service. */
const isDictHosted = (): boolean => typeof window.__webviewAction === 'function'

/**
 * Runs a named dictionary lookup on whichever leg is present. Both answer the same op names
 * with the same shapes — dev through the service, hosted through the WebView2 bridge into
 * KitveiHakodesh.Core — so a lookup cannot behave differently between the two apps.
 *
 * Arguments are NESTED under `params`: __webviewAction flattens whatever it is given onto the
 * message root, where `id` is already the bridge's correlation id.
 */
function dictOp<T>(op: string, args: object = {}): Promise<T> {
  if (!isDictHosted()) return serviceCall<T>(op, args)
  return (window.__webviewAction!('dictQuery', { op, params: args }) as Promise<{ error?: string } & T>)
    .then((reply) => {
      if (reply && reply.error) throw new Error(String(reply.error))
      return reply as T
    })
}

// ── Dictionary tier queries ───────────────────────────────────────────────────

async function dictExact(term: string): Promise<{ rows: SenseRow[]; isExact: boolean }> {
  return dictOp<{ rows: SenseRow[]; isExact: boolean }>('dictExact', { term })
}

async function dictPrefix(term: string): Promise<SenseRow[]> {
  return (await dictOp<{ rows: SenseRow[] }>('dictPrefix', { term })).rows
}

async function dictContains(term: string): Promise<SenseRow[]> {
  return (await dictOp<{ rows: SenseRow[] }>('dictContains', { term })).rows
}

// ── Exported dictionary functions ─────────────────────────────────────────────

/** Related words (ראו גם, נגזרות, ניגודים — excludes כתיב variants). */
export async function dictLinks(term: string): Promise<DictLink[]> {
  return (await dictOp<{ links: DictLink[] }>('dictLinks', { term })).links
}

/** Synonym words (נרדף). */
export async function dictSynonyms(term: string): Promise<string[]> {
  return (await dictOp<{ words: string[] }>('dictSynonyms', { term })).words
}

/** Spelling variants — same word different spelling (כתיב). */
export async function dictVariants(term: string): Promise<string[]> {
  return (await dictOp<{ words: string[] }>('dictVariants', { term })).words
}

/** Candidate headwords for spelling suggestions (Levenshtein). */
export async function dictSpellCandidates(term: string): Promise<string[]> {
  // The two-then-three character fragment probes, and the de-duplication across them, run
  // inside C# — this used to issue both queries and merge their rows here.
  return (await dictOp<{ words: string[] }>('dictSpellCandidates', { term })).words
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
  // The two passes — every candidate by exact match first, then every candidate by
  // %contains% — run inside C#, so this no longer walks the list a query at a time.
  const r = await dictOp<{ matched: string | null; rows: SenseRow[] }>('dictAbbrevSenses', { candidates })
  return r.matched ? { matched: r.matched, rows: r.rows } : null
}

/**
 * Given a list of candidate headwords (כתיב מלא expansions), returns only those
 * that actually exist in the word table. Single IN query — no sense data fetched.
 */
export async function dictKetivVariants(candidates: string[]): Promise<string[]> {
  if (candidates.length === 0) return []
  return (await dictOp<{ words: string[] }>('dictKetivVariants', { candidates })).words
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
