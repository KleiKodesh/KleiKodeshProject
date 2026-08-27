/**
 * Connection type constants, DB→canonical mapping, Hebrew label lookups, and the
 * lazy-loaded ID table. All connection-type knowledge lives here — nothing else in
 * the commentary feature should duplicate or re-derive these values.
 */
import { getAllConnectionTypes } from '@/webview-host/seforimApi'

export type CommentaryConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY' | 'EIN_MISHPAT' | 'OTHER' | 'REFERENCE'
export type StaticFilterConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY' | 'EIN_MISHPAT'

export const CONNECTION_TYPE_PRIORITY: CommentaryConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
  'EIN_MISHPAT',
  'OTHER',
  'REFERENCE',
]

const STATIC_FILTER_CONNECTION_TYPE_LIST: StaticFilterConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
  'EIN_MISHPAT',
]

export const STATIC_FILTER_CONNECTION_TYPES = new Set<StaticFilterConnectionType>(
  STATIC_FILTER_CONNECTION_TYPE_LIST,
)

/**
 * Maps raw connection type names from the DB to the canonical CommentaryConnectionType
 * used for grouping and display. New connection types added to the DB are mapped here
 * so older DB versions that lack them continue to work — the mapping is only applied
 * when the name is actually returned by the DB.
 *
 * COMMENTARY equivalents: SUPER_COMMENTARY, PARSHANUT, MIDRASH
 * EIN_MISHPAT maps to its own canonical type — displayed under עין משפט.
 * REFERENCE (ציונים) equivalents: MESORAH_HASHAS, MISHNAH_IN_TALMUD
 * All other unknown names fall back to OTHER (קשרים).
 */
const DB_CONNECTION_TYPE_TO_CANONICAL: Record<string, CommentaryConnectionType> = {
  SOURCE: 'SOURCE',
  TARGUM: 'TARGUM',
  COMMENTARY: 'COMMENTARY',
  SUPER_COMMENTARY: 'COMMENTARY',
  PARSHANUT: 'COMMENTARY',
  MIDRASH: 'COMMENTARY',
  REFERENCE: 'REFERENCE',
  MESORAH_HASHAS: 'REFERENCE',
  EIN_MISHPAT: 'EIN_MISHPAT',
  MISHNAH_IN_TALMUD: 'REFERENCE',
  OTHER: 'OTHER',
}

export function normalizeConnectionTypeName(dbName: string): CommentaryConnectionType {
  return DB_CONNECTION_TYPE_TO_CANONICAL[dbName] ?? 'OTHER'
}

export const CONNECTION_TYPE_SECTION_LABELS: Record<CommentaryConnectionType, string> = {
  SOURCE: 'מקושרים',
  TARGUM: 'תרגומים',
  COMMENTARY: 'מפרשים',
  EIN_MISHPAT: 'עין משפט',
  OTHER: 'קשרים',
  REFERENCE: 'ציונים',
}

// Reverse mapping: Hebrew label → connection type
export const SECTION_LABEL_TO_CONNECTION_TYPE: Record<string, CommentaryConnectionType> = {
  מקושרים: 'SOURCE',
  תרגומים: 'TARGUM',
  מפרשים: 'COMMENTARY',
  'עין משפט': 'EIN_MISHPAT',
  קשרים: 'OTHER',
  ציונים: 'REFERENCE',
}

// ── Lazy-loaded ID table ──────────────────────────────────────────────────────

let connectionTypeNamesById: Map<number, string> | null = null
let connectionTypeIdsByName: Map<string, number> | null = null

export async function ensureConnectionTypeNamesLoaded() {
  if (connectionTypeNamesById && connectionTypeIdsByName) return
  const rows = await getAllConnectionTypes()
  connectionTypeNamesById = new Map(rows.map((row) => [row.id, row.name]))
  connectionTypeIdsByName = new Map(rows.map((row) => [row.name, row.id]))
}

export function getConnectionTypeName(connectionTypeId: number): string {
  return connectionTypeNamesById?.get(connectionTypeId) ?? String(connectionTypeId)
}

export function getConnectionTypeId(connectionTypeName: string): number | null {
  return connectionTypeIdsByName?.get(connectionTypeName) ?? null
}

export function getPrimaryConnectionType(connectionTypes: string[]): string {
  for (const type of CONNECTION_TYPE_PRIORITY) {
    if (connectionTypes.includes(type)) return type
  }
  return connectionTypes[0] ?? 'OTHER'
}

export function isStaticFilterConnectionType(type: string): type is StaticFilterConnectionType {
  return STATIC_FILTER_CONNECTION_TYPES.has(type as StaticFilterConnectionType)
}

/**
 * Returns all canonical connection type names that map to the given canonical type.
 * Used when building SQL IN clauses that must match DB rows for a logical group
 * (e.g. all DB names that are treated as COMMENTARY).
 */
export function getDbNamesForCanonicalType(canonical: CommentaryConnectionType): string[] {
  return Object.entries(DB_CONNECTION_TYPE_TO_CANONICAL)
    .filter(([, mapped]) => mapped === canonical)
    .map(([dbName]) => dbName)
}

/**
 * Returns the IDs of all connection types in the DB that canonicalize to COMMENTARY.
 * Caller must call ensureConnectionTypeNamesLoaded first.
 */
export function getCommentaryConnectionTypeIds(): number[] {
  return getDbNamesForCanonicalType('COMMENTARY')
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)
}

/**
 * Connection type names that make a book DEPEND on another: everything that comments
 * on, translates, or expounds a base text. Reversing a link of one of these types is
 * what finds the base text of the current book.
 *
 * A commentary links to its base with COMMENTARY, but a targum links with TARGUM - so
 * a COMMENTARY-only reverse lookup finds nothing for a targum and its base book goes
 * missing. DIBUR_HAMATCHIL and EIN_MISHPAT are dependants in the same sense.
 * Mirrors the type list Zayit uses for its inverse-link (SOURCE) queries.
 */
const BASE_TEXT_REVERSE_DB_NAMES = [
  'COMMENTARY',
  'SUPER_COMMENTARY',
  'TARGUM',
  'MIDRASH',
  'PARSHANUT',
  'DIBUR_HAMATCHIL',
  'EIN_MISHPAT',
]

/**
 * Connection type IDs used to find the BASE book of the current one by looking at
 * links pointing AT it. Forward lookups keep using getCommentaryConnectionTypeIds.
 * Caller must call ensureConnectionTypeNamesLoaded first.
 */
export function getBaseTextReverseConnectionTypeIds(): number[] {
  return BASE_TEXT_REVERSE_DB_NAMES
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)
}
