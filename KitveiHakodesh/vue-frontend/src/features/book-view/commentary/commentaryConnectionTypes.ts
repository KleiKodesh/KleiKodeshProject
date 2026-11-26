/**
 * Connection type constants, DB→canonical mapping, Hebrew label lookups, and the
 * lazy-loaded ID table. All connection-type knowledge lives here — nothing else in
 * the commentary feature should duplicate or re-derive these values.
 */
import { getAllConnectionTypes } from '@/webview-host/seforimApi'

export type CommentaryConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY' | 'EIN_MISHPAT' | 'MESORAH_HASHAS' | 'OTHER' | 'REFERENCE'
export type StaticFilterConnectionType = 'SOURCE' | 'TARGUM' | 'COMMENTARY'

export const CONNECTION_TYPE_PRIORITY: CommentaryConnectionType[] = [
  'SOURCE',
  'TARGUM',
  'COMMENTARY',
  'EIN_MISHPAT',
  'MESORAH_HASHAS',
  'OTHER',
  'REFERENCE',
]

// EIN_MISHPAT is deliberately absent: it is a per-line reference apparatus (this line
// of gemara points at these halachot), not a book that stands in a fixed relationship
// to the current one. It is resolved per line with the rest of the commentary instead.
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
 * EIN_MISHPAT and MESORAH_HASHAS map to their own canonical types — both get priority
 * treatment (simplest decoration, plain) in word-link rendering.
 * REFERENCE equivalents: MISHNAH_IN_TALMUD
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
  EIN_MISHPAT: 'EIN_MISHPAT',
  MESORAH_HASHAS: 'MESORAH_HASHAS',
  MISHNAH_IN_TALMUD: 'REFERENCE',
  OTHER: 'OTHER',
}

export function normalizeConnectionTypeName(dbName: string): CommentaryConnectionType {
  return DB_CONNECTION_TYPE_TO_CANONICAL[dbName] ?? 'OTHER'
}

// Reverse mapping: Hebrew label → connection type
export const SECTION_LABEL_TO_CONNECTION_TYPE: Record<string, CommentaryConnectionType> = {
  מקושרים: 'SOURCE',
  תרגומים: 'TARGUM',
  מפרשים: 'COMMENTARY',
  'עין משפט': 'EIN_MISHPAT',
  'מסורת הש״ס': 'MESORAH_HASHAS',
  קשרים: 'OTHER',
  ציונים: 'REFERENCE',
}

// ── Lazy-loaded ID table ──────────────────────────────────────────────────────

let connectionTypeNamesById: Map<number, string> | null = null

export async function ensureConnectionTypeNamesLoaded() {
  if (connectionTypeNamesById) return
  const rows = await getAllConnectionTypes()
  connectionTypeNamesById = new Map(rows.map((row) => [row.id, row.name]))
}

export function getConnectionTypeName(connectionTypeId: number): string {
  return connectionTypeNamesById?.get(connectionTypeId) ?? String(connectionTypeId)
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

