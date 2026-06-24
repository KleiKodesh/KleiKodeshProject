/**
 * Connection type constants, DB→canonical mapping, Hebrew label lookups, and the
 * lazy-loaded ID table. All connection-type knowledge lives here — nothing else in
 * the commentary feature should duplicate or re-derive these values.
 */
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'

export type CommentaryConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY' | 'OTHER' | 'REFERENCE'
export type StaticFilterConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY'

export const CONNECTION_TYPE_PRIORITY: CommentaryConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
  'OTHER',
  'REFERENCE',
]

const STATIC_FILTER_CONNECTION_TYPE_LIST: StaticFilterConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
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
 * REFERENCE (ציונים) equivalents: MESORAH_HASHAS, EIN_MISHPAT, MISHNAH_IN_TALMUD
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
  EIN_MISHPAT: 'REFERENCE',
  MISHNAH_IN_TALMUD: 'REFERENCE',
  OTHER: 'OTHER',
}

export function normalizeConnectionTypeName(dbName: string): CommentaryConnectionType {
  return DB_CONNECTION_TYPE_TO_CANONICAL[dbName] ?? 'OTHER'
}

export const CONNECTION_TYPE_SECTION_LABELS: Record<CommentaryConnectionType, string> = {
  SOURCE: 'מקור',
  TARGUM: 'תרגומים',
  COMMENTARY: 'מפרשים',
  OTHER: 'קשרים',
  REFERENCE: 'ציונים',
}

// Reverse mapping: Hebrew label → connection type
export const SECTION_LABEL_TO_CONNECTION_TYPE: Record<string, CommentaryConnectionType> = {
  מקור: 'SOURCE',
  תרגומים: 'TARGUM',
  מפרשים: 'COMMENTARY',
  קשרים: 'OTHER',
  ציונים: 'REFERENCE',
}

// ── Lazy-loaded ID table ──────────────────────────────────────────────────────

let connectionTypeNamesById: Map<number, string> | null = null
let connectionTypeIdsByName: Map<string, number> | null = null

export async function ensureConnectionTypeNamesLoaded() {
  if (connectionTypeNamesById && connectionTypeIdsByName) return
  const rows = await query<{ id: number; name: string }>(SQL.GET_ALL_CONNECTION_TYPES)
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
 * Returns the IDs of all connection types in the DB that canonicalize to TARGUM.
 * Caller must call ensureConnectionTypeNamesLoaded first.
 */
export function getTargumConnectionTypeIds(): number[] {
  return getDbNamesForCanonicalType('TARGUM')
    .map((name) => getConnectionTypeId(name))
    .filter((id): id is number => id != null)
}
